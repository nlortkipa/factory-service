[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Launcher,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][uri]$ApiBaseUrl,
    [Parameter(Mandatory)][uri]$PayloadUrl,
    [Parameter(Mandatory)][string]$OperatorId,
    [Parameter(Mandatory)][string]$OperatorName,
    [Parameter(Mandatory)][string]$PlantName
)

$ErrorActionPreference = "Stop"
$source = [IO.Path]::GetFullPath($Launcher)
$target = [IO.Path]::GetFullPath($Output)
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Launcher not found: $source" }
if ($source -eq $target) { throw "Output must be different from the clean launcher." }

$configuration = [ordered]@{
    apiBaseUrl = $ApiBaseUrl.AbsoluteUri.TrimEnd('/')
    payloadUrl = $PayloadUrl.AbsoluteUri
    operatorId = $OperatorId
    operatorName = $OperatorName
    plantName = $PlantName
} | ConvertTo-Json -Compress

$executableBytes = [IO.File]::ReadAllBytes($source)
$configurationBytes = [Text.Encoding]::UTF8.GetBytes($configuration)
$markerBytes = [Text.Encoding]::ASCII.GetBytes("FSCONFIGv1")
$lengthBytes = [BitConverter]::GetBytes([int]$configurationBytes.Length)
$outputBytes = [byte[]]::new($executableBytes.Length + $configurationBytes.Length + $markerBytes.Length + $lengthBytes.Length)
[Buffer]::BlockCopy($executableBytes, 0, $outputBytes, 0, $executableBytes.Length)
[Buffer]::BlockCopy($configurationBytes, 0, $outputBytes, $executableBytes.Length, $configurationBytes.Length)
[Buffer]::BlockCopy($markerBytes, 0, $outputBytes, $executableBytes.Length + $configurationBytes.Length, $markerBytes.Length)
[Buffer]::BlockCopy($lengthBytes, 0, $outputBytes, $executableBytes.Length + $configurationBytes.Length + $markerBytes.Length, $lengthBytes.Length)

$targetDirectory = Split-Path -Parent $target
if ($targetDirectory) { [IO.Directory]::CreateDirectory($targetDirectory) | Out-Null }
[IO.File]::WriteAllBytes($target, $outputBytes)
Write-Host "Provisioned launcher: $target" -ForegroundColor Green
