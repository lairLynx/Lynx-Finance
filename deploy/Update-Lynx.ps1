<#
.SYNOPSIS
  Deploys the newest Lynx release on this host (run by a scheduled task; safe to run by hand).

.DESCRIPTION
  1. Finds the latest GitHub release (or uses -ZipPath / -Tag) and verifies its SHA-256.
  2. Extracts it to <Root>\releases\<tag>, stops the service, backs up the database,
     points <Root>\current at the new release and starts the service.
  3. Waits for /healthz to report the new version. If it does not, the previous release and the
     database backup are restored automatically.

  Deploying with -Tag pins that release: the scheduled task then leaves it alone until you run -Unpin.

  Layout under <Root>: current (junction), releases\, data\ (database + keys, never touched by deploys),
  backups\, logs\, config.json, current-release.txt.

  Works in Windows PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [string]$Root = 'C:\Apps\Lynx',
    # Deploy a local zip (first install, offline, testing) instead of asking GitHub.
    [string]$ZipPath,
    # Deploy a specific release tag instead of the latest (rollback to an older release).
    [string]$Tag,
    # Redeploy even if this release is already current.
    [switch]$Force,
    # Remove the pin left by -Tag so automatic updates resume.
    [switch]$Unpin
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocol]::Tls12 } catch { }

$configPath = Join-Path $Root 'config.json'
if (-not (Test-Path $configPath)) { throw "No config.json in $Root. Run Install-Lynx.ps1 first." }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$serviceName = $config.ServiceName
$port = [int]$config.LocalPort
$repo = $config.Repo
$keepReleases = if ($config.KeepReleases) { [int]$config.KeepReleases } else { 3 }
$keepBackups = if ($config.KeepBackups) { [int]$config.KeepBackups } else { 14 }

$releases = Join-Path $Root 'releases'
$data = Join-Path $Root 'data'
$backups = Join-Path $Root 'backups'
$downloads = Join-Path $Root 'downloads'
$logs = Join-Path $Root 'logs'
$current = Join-Path $Root 'current'
$stateFile = Join-Path $Root 'current-release.txt'
$pinFile = Join-Path $Root 'pinned-release.txt'
foreach ($d in $releases, $data, $backups, $downloads, $logs) { New-Item -ItemType Directory -Path $d -Force | Out-Null }

function Write-Log([string]$Message, [string]$Level = 'INFO') {
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $Level, $Message
    Add-Content -Path (Join-Path $logs 'deploy.log') -Value $line
    Write-Host $line
}

# One deploy at a time (scheduled task + manual run).
$mutex = New-Object System.Threading.Mutex($false, 'Global\LynxFinanceDeploy')
if (-not $mutex.WaitOne(0)) { Write-Host 'Another deploy is running; exiting.'; exit 0 }

function Get-ServiceOrNull { Get-Service -Name $serviceName -ErrorAction SilentlyContinue }

function Stop-LynxService {
    $svc = Get-ServiceOrNull
    if (-not $svc -or $svc.Status -eq 'Stopped') { return }
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and (Get-Service -Name $serviceName).Status -ne 'Stopped') { Start-Sleep -Milliseconds 500 }
    if ((Get-Service -Name $serviceName).Status -ne 'Stopped') {
        Write-Log 'Service did not stop in 30s; killing the process.' 'WARN'
        Get-Process -Name 'Lynx.Api' -ErrorAction SilentlyContinue | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
}

function Start-LynxService {
    if (Get-ServiceOrNull) { Start-Service -Name $serviceName }
}

# Replaces the "current" junction. rmdir on a junction removes only the link, never the release it points to.
function Set-Current([string]$Target) {
    if (Test-Path $current) { & cmd.exe /c rmdir "`"$current`"" | Out-Null }
    New-Item -ItemType Junction -Path $current -Target $Target | Out-Null
}

function Test-Healthy([string]$ExpectedVersion, [int]$TimeoutSeconds = 60) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-RestMethod -Uri "http://127.0.0.1:$port/healthz" -TimeoutSec 5
            if ($r.status -eq 'ok' -and (-not $ExpectedVersion -or $r.version -eq $ExpectedVersion)) { return $true }
        }
        catch { }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Backup-Database([string]$Label) {
    $db = Join-Path $data 'lynx.db'
    if (-not (Test-Path $db)) { return $null }
    $dir = Join-Path $backups ('{0:yyyyMMdd-HHmmss}-before-{1}' -f (Get-Date), $Label)
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
    foreach ($f in 'lynx.db', 'lynx.db-wal', 'lynx.db-shm') {
        $p = Join-Path $data $f
        if (Test-Path $p) { Copy-Item $p $dir }
    }
    return $dir
}

function Restore-Database([string]$BackupDir) {
    if (-not $BackupDir -or -not (Test-Path $BackupDir)) { return }
    foreach ($f in 'lynx.db', 'lynx.db-wal', 'lynx.db-shm') { Remove-Item (Join-Path $data $f) -Force -ErrorAction SilentlyContinue }
    Get-ChildItem $BackupDir -File | Copy-Item -Destination $data
}

try {
    if ($Unpin -and (Test-Path $pinFile)) { Remove-Item $pinFile -Force; Write-Log 'Pin removed; automatic updates resume.' }
    if ((Test-Path $pinFile) -and -not $Tag -and -not $ZipPath) { exit 0 } # pinned by an earlier manual -Tag deploy

    # ---- 1. Decide what to deploy -------------------------------------------------------------------------
    $previousTag = if (Test-Path $stateFile) { (Get-Content $stateFile -Raw).Trim() } else { $null }
    $zip = $null

    if ($ZipPath) {
        if (-not (Test-Path $ZipPath)) { throw "Zip not found: $ZipPath" }
        $zip = (Resolve-Path $ZipPath).Path
        $newTag = 'local-{0:yyyyMMdd-HHmmss}' -f (Get-Date)
    }
    else {
        $headers = @{ 'User-Agent' = 'lynx-updater'; 'Accept' = 'application/vnd.github+json' }
        $url = if ($Tag) { "https://api.github.com/repos/$repo/releases/tags/$Tag" } else { "https://api.github.com/repos/$repo/releases/latest" }
        try { $release = Invoke-RestMethod -Uri $url -Headers $headers -TimeoutSec 30 }
        catch {
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { Write-Host 'No release published yet.'; exit 0 }
            throw
        }

        $newTag = $release.tag_name
        if ($newTag -notmatch '^[A-Za-z0-9._-]+$') { throw "Unexpected release tag '$newTag'." }
        if ($newTag -eq $previousTag -and -not $Force) { exit 0 } # up to date; stay quiet so the log stays readable

        $zipAsset = $release.assets | Where-Object { $_.name -like 'lynx-*.zip' } | Select-Object -First 1
        $shaAsset = $release.assets | Where-Object { $_.name -like 'lynx-*.zip.sha256' } | Select-Object -First 1
        if (-not $zipAsset -or -not $shaAsset) { throw "Release $newTag has no zip + sha256 assets." }

        Write-Log "New release $newTag (current: $previousTag). Downloading $($zipAsset.name)..."
        $zip = Join-Path $downloads $zipAsset.name
        Invoke-WebRequest -Uri $zipAsset.browser_download_url -OutFile $zip -Headers @{ 'User-Agent' = 'lynx-updater' } -TimeoutSec 600
        $expected = ((Invoke-RestMethod -Uri $shaAsset.browser_download_url -Headers @{ 'User-Agent' = 'lynx-updater' }) -split '\s+')[0].Trim().ToLowerInvariant()
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected -ne $actual) { Remove-Item $zip -Force; throw "Checksum mismatch for $($zipAsset.name): expected $expected, got $actual." }
    }

    # ---- 2. Extract ---------------------------------------------------------------------------------------
    $target = Join-Path $releases $newTag
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $target -Force
    if (-not (Test-Path (Join-Path $target 'Lynx.Api.exe'))) { throw "Package does not contain Lynx.Api.exe." }
    $versionFile = Join-Path $target 'version.txt'
    $expectedVersion = if (Test-Path $versionFile) { (Get-Content $versionFile -Raw).Trim() } else { $null }

    # ---- 3. Swap ------------------------------------------------------------------------------------------
    Write-Log "Deploying $newTag..."
    Stop-LynxService
    $backup = Backup-Database $newTag
    if ($backup) { Write-Log "Database backed up to $backup" }
    Set-Current $target
    Start-LynxService

    if (-not (Get-ServiceOrNull)) {
        Write-Log "Service '$serviceName' is not installed; files are in place at $current. Run Install-Lynx.ps1." 'WARN'
    }
    elseif (Test-Healthy $expectedVersion 60) {
        Set-Content -Path $stateFile -Value $newTag -Encoding ASCII
        if ($Tag) { Set-Content -Path $pinFile -Value $Tag -Encoding ASCII; Write-Log "Pinned to $Tag; run with -Unpin to resume automatic updates." }
        Write-Log "Release $newTag is healthy."
    }
    else {
        # ---- 4. Roll back ---------------------------------------------------------------------------------
        Write-Log "Release $newTag did not become healthy. Rolling back." 'ERROR'
        Stop-LynxService
        $previousDir = if ($previousTag) { Join-Path $releases $previousTag } else { $null }
        if ($previousDir -and (Test-Path $previousDir)) {
            Restore-Database $backup
            Set-Current $previousDir
            Start-LynxService
            if (Test-Healthy $null 60) { Write-Log "Rolled back to $previousTag." 'WARN' }
            else { Write-Log "Rollback to $previousTag is not healthy either. Check the Windows Event Log (Application, source $serviceName)." 'ERROR' }
        }
        else {
            Write-Log 'No previous release to roll back to; the service is left stopped.' 'ERROR'
        }
        exit 1
    }

    # ---- 5. Tidy up ---------------------------------------------------------------------------------------
    Get-ChildItem $releases -Directory | Sort-Object LastWriteTime -Descending | Select-Object -Skip $keepReleases |
        Where-Object { $_.Name -ne $newTag -and $_.Name -ne $previousTag } | Remove-Item -Recurse -Force
    Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -Skip $keepBackups | Remove-Item -Recurse -Force
    Get-ChildItem $downloads -File -ErrorAction SilentlyContinue | Remove-Item -Force
}
catch {
    Write-Log "Deploy failed: $($_.Exception.Message)" 'ERROR'
    exit 1
}
finally {
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
