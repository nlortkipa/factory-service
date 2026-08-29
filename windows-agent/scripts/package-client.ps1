[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Configuration = "Release",

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9.-]*$')]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$agentRoot = Split-Path -Parent $PSScriptRoot
$packageSource = Join-Path $agentRoot "client-package"
$distRoot = Join-Path $agentRoot "dist"
$packageName = "FactoryServiceClient-$Runtime"
$packageRoot = Join-Path $distRoot $packageName
$payloadRoot = Join-Path $packageRoot "payload"
$publishRoot = Join-Path $agentRoot "artifacts\$packageName"
$servicePublish = Join-Path $publishRoot "service"
$companionPublish = Join-Path $publishRoot "companion"
$serviceProject = Join-Path $agentRoot "src\FactoryService.Service\FactoryService.Service.csproj"
$companionProject = Join-Path $agentRoot "src\FactoryService.Companion\FactoryService.Companion.csproj"
$zipPath = Join-Path $distRoot "$packageName.zip"

$agentRootPrefix = [IO.Path]::GetFullPath($agentRoot).TrimEnd('\') + '\'
function Assert-UnderAgentRoot {
    param([Parameter(Mandatory)][string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($agentRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the Windows agent directory: $fullPath"
    }
}

foreach ($path in @($packageRoot, $publishRoot)) {
    Assert-UnderAgentRoot -Path $path
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
$packageReady = $false

try {
    dotnet publish $serviceProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $servicePublish
    if ($LASTEXITCODE -ne 0) { throw "Service publish failed with exit code $LASTEXITCODE." }

    dotnet publish $companionProject `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $companionPublish
    if ($LASTEXITCODE -ne 0) { throw "Companion publish failed with exit code $LASTEXITCODE." }

    Copy-Item -Path (Join-Path $servicePublish "*") -Destination $payloadRoot -Recurse -Force
    Copy-Item -Path (Join-Path $companionPublish "*") -Destination $payloadRoot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $agentRoot "scripts\companion-login-guard.cmd") -Destination (Join-Path $payloadRoot "FactoryService.CompanionGuard.cmd") -Force
    foreach ($packageFile in @("Install.cmd", "Uninstall.cmd", "instruction.txt")) {
        Copy-Item -LiteralPath (Join-Path $packageSource $packageFile) -Destination (Join-Path $packageRoot $packageFile) -Force
    }

    $requiredFiles = @(
        "payload\FactoryService.Service.exe",
        "payload\FactoryService.Companion.exe",
        "payload\FactoryService.CompanionGuard.cmd",
        "Install.cmd",
        "Uninstall.cmd",
        "instruction.txt"
    )
    foreach ($requiredFile in $requiredFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageRoot $requiredFile))) {
            throw "Package validation failed. Missing $requiredFile."
        }
    }

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $archiveEntries = @($archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") })
        foreach ($requiredFile in $requiredFiles) {
            $entryName = $requiredFile.Replace("\", "/")
            if ($entryName -notin $archiveEntries) {
                throw "ZIP validation failed. Missing $entryName."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $zipStream = [IO.File]::OpenRead($zipPath)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $hash = [BitConverter]::ToString($sha256.ComputeHash($zipStream)).Replace("-", "")
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $zipStream.Dispose()
    }

    $sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
    Write-Host "Client package ready." -ForegroundColor Green
    Write-Host "Path: $zipPath"
    Write-Host "Size: $sizeMb MB"
    Write-Host "SHA256: $hash"
    $packageReady = $true
}
finally {
    foreach ($path in @($packageRoot, $publishRoot)) {
        Assert-UnderAgentRoot -Path $path
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }
    }
    if (-not $packageReady -and (Test-Path -LiteralPath $zipPath)) {
        Remove-Item -LiteralPath $zipPath -Force
    }
}
