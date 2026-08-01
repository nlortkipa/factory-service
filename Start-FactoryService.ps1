[CmdletBinding()]
param(
    [switch]$NoCompanion
)

$ErrorActionPreference = "Stop"
$WorkspaceRoot = Split-Path -Parent $PSCommandPath
$StateDirectory = Join-Path $WorkspaceRoot ".dev"
New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null

function Test-Url {
    param([Parameter(Mandatory)][string]$Uri)

    try {
        Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-ForUrl {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Name,
        [int]$TimeoutSeconds = 45
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Url -Uri $Uri) {
            return
        }

        Start-Sleep -Milliseconds 500
    }

    throw "$Name did not become ready at $Uri within $TimeoutSeconds seconds. Check .dev logs."
}

$started = [System.Collections.Generic.List[object]]::new()
$stateFile = Join-Path $StateDirectory "processes.json"
if (Test-Path -LiteralPath $stateFile) {
    foreach ($record in @(Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json)) {
        $started.Add($record)
    }
}

function Save-StartedProcesses {
    if ($started.Count -gt 0) {
        $started | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }
}

if (-not (Test-Url -Uri "http://127.0.0.1:5106/health")) {
    $backendProject = Join-Path $WorkspaceRoot "backend\src\FactoryService.Api\FactoryService.Api.csproj"
    $backendArguments = "run --project `"$backendProject`" --launch-profile http"
    $backendProcess = Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $backendArguments `
        -WorkingDirectory $WorkspaceRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $StateDirectory "backend.out.log") `
        -RedirectStandardError (Join-Path $StateDirectory "backend.err.log") `
        -PassThru
    $started.Add([pscustomobject]@{
        Kind = "backend"
        Pid = $backendProcess.Id
        StartedAtUtc = $backendProcess.StartTime.ToUniversalTime().ToString("O")
    })
    Save-StartedProcesses
}

Wait-ForUrl -Uri "http://127.0.0.1:5106/health" -Name "Backend"

if (-not (Test-Url -Uri "http://127.0.0.1:5173")) {
    $frontendDirectory = Join-Path $WorkspaceRoot "frontend"
    if (-not (Test-Path -LiteralPath (Join-Path $frontendDirectory "node_modules"))) {
        Push-Location $frontendDirectory
        try {
            & npm.cmd install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install failed with exit code $LASTEXITCODE."
            }
        }
        finally {
            Pop-Location
        }
    }

    $frontendProcess = Start-Process `
        -FilePath "npm.cmd" `
        -ArgumentList "run dev -- --host 0.0.0.0" `
        -WorkingDirectory $frontendDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $StateDirectory "frontend.out.log") `
        -RedirectStandardError (Join-Path $StateDirectory "frontend.err.log") `
        -PassThru
    $started.Add([pscustomobject]@{
        Kind = "frontend"
        Pid = $frontendProcess.Id
        StartedAtUtc = $frontendProcess.StartTime.ToUniversalTime().ToString("O")
    })
    Save-StartedProcesses
}

Wait-ForUrl -Uri "http://127.0.0.1:5173" -Name "Frontend"

if (-not $NoCompanion -and -not (Get-Process -Name "FactoryService.Companion" -ErrorAction SilentlyContinue)) {
    $companionProject = Join-Path $WorkspaceRoot "windows-agent\src\FactoryService.Companion\FactoryService.Companion.csproj"
    & dotnet build $companionProject --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "The Windows companion build failed with exit code $LASTEXITCODE."
    }

    $companionDirectory = Join-Path $WorkspaceRoot "windows-agent\src\FactoryService.Companion\bin\Debug\net10.0-windows"
    $companionExecutable = Join-Path $companionDirectory "FactoryService.Companion.exe"
    Start-Process -FilePath $companionExecutable -WorkingDirectory $companionDirectory | Out-Null
}

Write-Host "Factory Service is ready." -ForegroundColor Green
Write-Host "Admin:  http://localhost:5173/admin"
Write-Host "API:    http://localhost:5106/health"
if (-not $NoCompanion) {
    Write-Host "QR display: running on this screen"
}
