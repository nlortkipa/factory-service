[CmdletBinding()]
param(
    [string]$InstallDirectory = "$env:ProgramFiles\Factory Service Agent"
)

$ErrorActionPreference = "Stop"
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell window."
}

Remove-ItemProperty `
    -Path "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "FactoryServiceCompanion" `
    -ErrorAction SilentlyContinue
Get-Process -Name "FactoryService.Companion" -ErrorAction SilentlyContinue | Stop-Process -Force

$service = Get-Service -Name "FactoryServiceAgent" -ErrorAction SilentlyContinue
if ($null -ne $service) {
    if ($service.Status -ne "Stopped") {
        Stop-Service -Name "FactoryServiceAgent" -Force
    }
    sc.exe delete "FactoryServiceAgent" | Out-Null
}

Write-Host "Service and companion startup entry removed. Installed files remain at $InstallDirectory."
