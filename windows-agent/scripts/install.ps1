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

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

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

if ($null -eq $existingService) {
    New-Service -Name $serviceName -BinaryPathName ('"' + $serviceExe + '"') -DisplayName "Factory Service Agent" -StartupType Automatic
}
Start-Service -Name $serviceName

$companionExe = Join-Path $InstallDirectory "FactoryService.Companion.exe"
$runKey = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run"
New-ItemProperty `
    -Path $runKey `
    -Name "FactoryServiceCompanion" `
    -Value ('"' + $companionExe + '"') `
    -PropertyType String `
    -Force | Out-Null

Start-Process -FilePath $companionExe
Write-Host "Factory Service Agent installed for the machine and all user logons."
