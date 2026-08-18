[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\Factory Service Agent",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot "artifacts"
$servicePublish = Join-Path $artifactRoot "service"
$companionPublish = Join-Path $artifactRoot "companion"
$serviceProject = Join-Path $projectRoot "src\FactoryService.Service\FactoryService.Service.csproj"
$companionProject = Join-Path $projectRoot "src\FactoryService.Companion\FactoryService.Companion.csproj"
$guardSource = Join-Path $projectRoot "scripts\companion-login-guard.cmd"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

# The vendor application and this companion cannot own the panel's serial port at the same time.
# Keep its task installed so this change stays reversible, but disable autostart.
$turmoTask = Get-ScheduledTask -TaskName "TURMO_ScheduleTask" -ErrorAction SilentlyContinue
if ($null -ne $turmoTask) {
    Disable-ScheduledTask -InputObject $turmoTask | Out-Null
}
Get-Process -Name "TURMO" -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet publish $serviceProject -c $Configuration -r $Runtime --self-contained true -o $servicePublish
dotnet publish $companionProject -c $Configuration -r $Runtime --self-contained true -o $companionPublish

$serviceName = "FactoryServiceAgent"
$serviceExe = Join-Path $InstallDirectory "FactoryService.Service.exe"
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existingService -and $existingService.Status -ne "Stopped") {
    Stop-Service -Name $serviceName -Force
}
Get-Process -Name "FactoryService.Companion" -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force -Path $InstallDirectory | Out-Null
Copy-Item -Path (Join-Path $servicePublish "*") -Destination $InstallDirectory -Recurse -Force
Copy-Item -Path (Join-Path $companionPublish "*") -Destination $InstallDirectory -Recurse -Force
Copy-Item -LiteralPath $guardSource -Destination (Join-Path $InstallDirectory "FactoryService.CompanionGuard.cmd") -Force

if ($null -eq $existingService) {
    New-Service -Name $serviceName -BinaryPathName ('"' + $serviceExe + '"') -DisplayName "Factory Service Agent" -StartupType Automatic
}
sc.exe description $serviceName "Kartuli Cement factory service and session monitor." | Out-Null
sc.exe config $serviceName start= delayed-auto | Out-Null
sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/restart/30000 | Out-Null
Start-Service -Name $serviceName

$companionExe = Join-Path $InstallDirectory "FactoryService.Companion.exe"
$companionGuard = Join-Path $InstallDirectory "FactoryService.CompanionGuard.cmd"
$runKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
New-ItemProperty `
    -Path $runKey `
    -Name "FactoryServiceCompanion" `
    -Value ('cmd.exe /d /c call "' + $companionGuard + '"') `
    -PropertyType String `
    -Force | Out-Null

Start-Process -FilePath $companionGuard -WindowStyle Hidden
Write-Host "Factory Service Agent installed for the machine and all user logons."
if ($null -ne $turmoTask) {
    Write-Host "Disabled TURMO_ScheduleTask so the factory companion can use the Turmo panel."
}
