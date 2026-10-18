<#
.SYNOPSIS
  Builds a deployable release zip: Angular UI + self-contained .NET API for win-x64.

.DESCRIPTION
  Used by the GitHub workflow and runnable locally to produce the same package. Output (in -OutDir):
    lynx-<short version>.zip         the release
    lynx-<short version>.zip.sha256  checksum the host verifies before deploying

  The package contains the API, wwwroot (the UI), version.txt and the deploy scripts under deploy\.
#>
[CmdletBinding()]
param(
    # Full commit hash by default. It is stamped into the app and reported by /healthz.
    [string]$Version,
    [string]$OutDir,
    [switch]$SkipTests,
    # Reuse the existing web/lynx-web/node_modules instead of `npm ci` (e.g. while `ng serve` has it locked).
    [switch]$SkipInstall
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $repo 'artifacts' }
if (-not $Version) { $Version = (& git -C $repo rev-parse HEAD).Trim() }
if ($Version -notmatch '^[0-9A-Za-z._-]+$') { throw "Version '$Version' contains unexpected characters." }
$short = if ($Version.Length -gt 7) { $Version.Substring(0, 7) } else { $Version }

function Invoke-Step([string]$Name, [scriptblock]$Action) {
    Write-Host "==> $Name" -ForegroundColor Cyan
    # Windows PowerShell 5.1 turns any native stderr output (npm warnings) into an error under 'Stop'; trust the exit code instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Action | ForEach-Object { "$_" } | Write-Host }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit code $LASTEXITCODE)" }
}

if (-not $SkipTests) {
    Invoke-Step 'dotnet test' { dotnet test (Join-Path $repo 'Lynx.sln') -c Release --nologo }
}

$web = Join-Path $repo 'web\lynx-web'
Push-Location $web
try {
    if (-not $SkipInstall) { Invoke-Step 'npm ci' { npm.cmd ci --no-audit --no-fund } }
    Invoke-Step 'ng build' { npm.cmd run build }
}
finally { Pop-Location }

$browser = Join-Path $web 'dist\lynx-web\browser'
if (-not (Test-Path (Join-Path $browser 'index.html'))) { throw "Angular build output not found at $browser" }

$wwwroot = Join-Path $repo 'src\Lynx.Api\wwwroot'
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
Copy-Item $browser $wwwroot -Recurse

$stage = Join-Path $OutDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Invoke-Step 'dotnet publish' {
    dotnet publish (Join-Path $repo 'src\Lynx.Api\Lynx.Api.csproj') -c Release -r win-x64 --self-contained true -o $stage `
        "-p:InformationalVersion=$Version" -p:IncludeSourceRevisionInInformationalVersion=false --nologo
}

# Never ship a developer database or secrets.
Get-ChildItem $stage -Recurse -Include '*.db', '*.db-wal', '*.db-shm', 'appsettings.Development.json' -ErrorAction SilentlyContinue | Remove-Item -Force

Copy-Item (Join-Path $repo 'deploy') (Join-Path $stage 'deploy') -Recurse
Set-Content -Path (Join-Path $stage 'version.txt') -Value $Version -Encoding ASCII -NoNewline

$zip = Join-Path $OutDir "lynx-$short.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding ASCII
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Release package: $zip" -ForegroundColor Green
Write-Host ("Size: {0:N1} MB   SHA256: {1}" -f ((Get-Item $zip).Length / 1MB), $hash)
