<#
.SYNOPSIS
  One-time (and safely re-runnable) setup of Lynx Finance on a Windows host behind Tailscale.

.DESCRIPTION
  - Creates C:\Apps\Lynx (releases, data, backups, logs) with locked-down permissions.
  - Optionally imports your existing database (-DatabasePath) before the first start.
  - Installs the Windows service "LynxFinance" (virtual account, loopback only, auto-restart).
  - Deploys the latest release (or -ZipPath) and waits until /healthz is OK.
  - Publishes it on the tailnet with Tailscale Serve on a dedicated port (-TailnetPort).
  - Registers a scheduled task that checks for new releases every 5 minutes.

  Run from an elevated PowerShell. Works in Windows PowerShell 5.1.

.EXAMPLE
  .\Install-Lynx.ps1 -DatabasePath D:\copy\lynx.db -TailnetPort 8443
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Root = 'C:\Apps\Lynx',
    [string]$Repo = 'lairLynx/Lynx-Finance',
    [string]$ServiceName = 'LynxFinance',
    # Port Kestrel listens on, loopback only. Give every project on this machine its own.
    [int]$LocalPort = 5180,
    # Port exposed on the tailnet (https://<this-machine>.<tailnet>.ts.net:<port>).
    [int]$TailnetPort = 8443,
    # Https: Tailscale terminates TLS with a real certificate (needs HTTPS enabled for the tailnet).
    # Tcp:   plain HTTP over the encrypted tailnet. None: do not touch Tailscale.
    [ValidateSet('Https', 'Tcp', 'None')]
    [string]$ServeMode = 'Https',
    # An existing lynx.db to start from (stop the dev API first so the file is complete).
    [string]$DatabasePath,
    # Deploy this local release zip instead of downloading the latest GitHub release.
    [string]$ZipPath,
    [switch]$SkipScheduledTask
)

$ErrorActionPreference = 'Stop'
function Step([string]$Message) { Write-Host "==> $Message" -ForegroundColor Cyan }

if ($LocalPort -lt 1024 -or $LocalPort -gt 65535) { throw 'LocalPort must be between 1024 and 65535.' }
if ($TailnetPort -lt 1 -or $TailnetPort -gt 65535) { throw 'TailnetPort must be between 1 and 65535.' }

# ---- Layout and permissions ------------------------------------------------------------------------------
Step "Creating $Root"
foreach ($d in 'releases', 'data', 'backups', 'logs', 'downloads') { New-Item -ItemType Directory -Path (Join-Path $Root $d) -Force | Out-Null }

$account = "NT SERVICE\$ServiceName"
# Only SYSTEM, administrators and the service itself may touch the app; the database is not world-readable.
& icacls $Root /inheritance:r /grant:r 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' "${account}:(OI)(CI)RX" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'icacls failed (root).' }

@{
    Repo         = $Repo
    ServiceName  = $ServiceName
    LocalPort    = $LocalPort
    TailnetPort  = $TailnetPort
    KeepReleases = 3
    KeepBackups  = 14
} | ConvertTo-Json | Set-Content -Path (Join-Path $Root 'config.json') -Encoding UTF8

# ---- Existing database -----------------------------------------------------------------------------------
$dataDir = Join-Path $Root 'data'
if ($DatabasePath) {
    Step 'Importing existing database'
    if (-not (Test-Path $DatabasePath)) { throw "Database not found: $DatabasePath" }
    if (Test-Path (Join-Path $dataDir 'lynx.db')) { throw "$dataDir\lynx.db already exists; refusing to overwrite it. Move it away first." }
    Copy-Item $DatabasePath (Join-Path $dataDir 'lynx.db')
    foreach ($suffix in '-wal', '-shm') { if (Test-Path "$DatabasePath$suffix") { Copy-Item "$DatabasePath$suffix" (Join-Path $dataDir "lynx.db$suffix") } }
}

# ---- Service ---------------------------------------------------------------------------------------------
Step "Configuring service $ServiceName"
$exe = Join-Path $Root 'current\Lynx.Api.exe'
$binPath = "`"$exe`""
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'Lynx Finance' `
        -Description 'Lynx personal finance (API + web UI), reachable only through Tailscale.' -StartupType Automatic | Out-Null
}
else {
    & sc.exe config $ServiceName binPath= $binPath | Out-Null
}
& sc.exe config $ServiceName obj= $account start= delayed-auto | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/30000/restart/60000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

# The service account needs to write the database and key ring, nothing else.
& icacls $dataDir /grant "${account}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'icacls failed (data).' }
& icacls $dataDir /grant:r 'SYSTEM:(OI)(CI)F' 'Administrators:(OI)(CI)F' | Out-Null

$secureCookies = if ($ServeMode -eq 'Https') { 'true' } else { 'false' }
$environment = @(
    'ASPNETCORE_ENVIRONMENT=Production',
    "ASPNETCORE_URLS=http://127.0.0.1:$LocalPort",
    "Lynx__DataDir=$dataDir",
    "Lynx__SecureCookies=$secureCookies"
)
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" -Name Environment -Value $environment -Type MultiString

if (-not [System.Diagnostics.EventLog]::SourceExists($ServiceName)) {
    [System.Diagnostics.EventLog]::CreateEventSource($ServiceName, 'Application')
}

# ---- Deploy ----------------------------------------------------------------------------------------------
Step 'Deploying the release'
$update = Join-Path $PSScriptRoot 'Update-Lynx.ps1'
if ($ZipPath) { & $update -Root $Root -ZipPath $ZipPath -Force } else { & $update -Root $Root -Force }
if ($LASTEXITCODE -ne 0) { throw "Deploy failed. See $Root\logs\deploy.log" }
if (-not (Test-Path (Join-Path $Root 'current-release.txt'))) {
    throw "No release was deployed. Publish one first (push to the prod branch) or pass -ZipPath. See $Root\logs\deploy.log"
}

# ---- Tailscale -------------------------------------------------------------------------------------------
$tailscale = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
if (-not $tailscale -and (Test-Path "$env:ProgramFiles\Tailscale\tailscale.exe")) { $tailscale = "$env:ProgramFiles\Tailscale\tailscale.exe" }
$url = "http://127.0.0.1:$LocalPort (this machine only)"

if ($ServeMode -ne 'None') {
    Step "Publishing on the tailnet (port $TailnetPort, $ServeMode)"
    if (-not $tailscale) { throw 'tailscale.exe not found. Install Tailscale and log in, or re-run with -ServeMode None.' }
    $target = "http://127.0.0.1:$LocalPort"
    if ($ServeMode -eq 'Https') { & $tailscale serve --bg "--https=$TailnetPort" $target }
    else { & $tailscale serve --bg "--tcp=$TailnetPort" "tcp://127.0.0.1:$LocalPort" }
    if ($LASTEXITCODE -ne 0) {
        throw "tailscale serve failed. For Https mode enable MagicDNS and HTTPS certificates in the Tailscale admin console (DNS page), or use -ServeMode Tcp."
    }
    try {
        $status = & $tailscale status --json | ConvertFrom-Json
        $dns = $status.Self.DNSName.TrimEnd('.')
        $scheme = if ($ServeMode -eq 'Https') { 'https' } else { 'http' }
        $url = "${scheme}://${dns}:$TailnetPort"
    }
    catch { $url = "(see: tailscale serve status)" }
}

# ---- Scheduled auto-update -------------------------------------------------------------------------------
if (-not $SkipScheduledTask) {
    Step 'Registering the auto-update task (every 5 minutes)'
    $script = Join-Path $Root 'current\deploy\Update-Lynx.ps1'
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$script`" -Root `"$Root`""
    $triggers = @(
        (New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)),
        (New-ScheduledTaskTrigger -AtStartup)
    )
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 15) -MultipleInstances IgnoreNew -StartWhenAvailable
    Register-ScheduledTask -TaskName "$ServiceName auto-update" -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Force | Out-Null
}

Write-Host ''
Write-Host 'Lynx Finance is running.' -ForegroundColor Green
Write-Host "  Open: $url"
Write-Host "  Logs: $Root\logs\deploy.log and Windows Event Viewer > Application > $ServiceName"
Write-Host "  Data: $dataDir (back this folder up)"
