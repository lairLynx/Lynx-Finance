<#
.SYNOPSIS
  Removes the service, scheduled task and Tailscale Serve entry. The data folder is kept unless -DeleteData is given.
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$Root = 'C:\Apps\Lynx',
    [string]$ServiceName = 'LynxFinance',
    [int]$TailnetPort = 8443,
    [switch]$DeleteData
)

$ErrorActionPreference = 'Continue'

$config = Join-Path $Root 'config.json'
if (Test-Path $config) { $TailnetPort = [int](Get-Content $config -Raw | ConvertFrom-Json).TailnetPort }

Unregister-ScheduledTask -TaskName "$ServiceName auto-update" -Confirm:$false -ErrorAction SilentlyContinue

$tailscale = (Get-Command tailscale -ErrorAction SilentlyContinue).Source
if (-not $tailscale -and (Test-Path "$env:ProgramFiles\Tailscale\tailscale.exe")) { $tailscale = "$env:ProgramFiles\Tailscale\tailscale.exe" }
if ($tailscale) {
    & $tailscale serve "--https=$TailnetPort" off 2>$null
    & $tailscale serve "--tcp=$TailnetPort" off 2>$null
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $ServiceName | Out-Null
}

if (Test-Path (Join-Path $Root 'current')) { & cmd.exe /c rmdir "`"$(Join-Path $Root 'current')`"" | Out-Null }
foreach ($d in 'releases', 'downloads') { Remove-Item (Join-Path $Root $d) -Recurse -Force -ErrorAction SilentlyContinue }

if ($DeleteData) { Remove-Item $Root -Recurse -Force }
else { Write-Host "Kept $Root\data and $Root\backups. Delete them yourself if you no longer need them." }
Write-Host 'Lynx Finance removed.'
