[CmdletBinding()]
param(
    [Alias("NoCompanion")]
    [switch]$NoClient
)

$ErrorActionPreference = "Stop"
$WorkspaceRoot = Split-Path -Parent $PSCommandPath
$StateDirectory = Join-Path $WorkspaceRoot ".dev"
New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null

$localEnvironmentFile = Join-Path $WorkspaceRoot ".env.local"
if (Test-Path -LiteralPath $localEnvironmentFile) {
    foreach ($line in Get-Content -LiteralPath $localEnvironmentFile) {
        if ($line -match '^\s*([^#=\s]+)\s*=\s*(.*)\s*$') {
            $name = $matches[1]
            $value = $matches[2].Trim().Trim('"').Trim("'")
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
}

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
    $savedRecords = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
    foreach ($record in $savedRecords) {
        if (
            $null -eq $record.PSObject.Properties["Kind"] -or
            $null -eq $record.PSObject.Properties["Pid"] -or
            $null -eq $record.PSObject.Properties["StartedAtUtc"]
        ) {
            continue
        }

        $process = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            continue
        }

        try {
            $recordedStart = if ($record.StartedAtUtc -is [DateTime]) {
                $record.StartedAtUtc.ToUniversalTime()
            }
            else {
                [DateTimeOffset]::Parse($record.StartedAtUtc).UtcDateTime
            }
        }
        catch {
            continue
        }
        if ([Math]::Abs(($process.StartTime.ToUniversalTime() - $recordedStart).TotalSeconds) -le 2) {
            $started.Add($record)
        }
    }
}

function Save-StartedProcesses {
    if ($started.Count -gt 0) {
        ConvertTo-Json -InputObject @($started.ToArray()) | Set-Content -LiteralPath $stateFile -Encoding UTF8
    }
    elseif (Test-Path -LiteralPath $stateFile) {
        Remove-Item -LiteralPath $stateFile -Force
    }
}

function Add-StartedProcess {
    param(
        [Parameter(Mandatory)][string]$Kind,
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process
    )

    $started.Add([pscustomobject]@{
        Kind = $Kind
        Pid = $Process.Id
        StartedAtUtc = $Process.StartTime.ToUniversalTime().ToString("O")
    })
    Save-StartedProcesses
}

Save-StartedProcesses

if (-not (Test-Url -Uri "http://127.0.0.1:5000/health")) {
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
    Add-StartedProcess -Kind "backend" -Process $backendProcess
}

Wait-ForUrl -Uri "http://127.0.0.1:5000/health" -Name "Backend"

if (-not (Test-Url -Uri "http://127.0.0.1:5173")) {
    $frontendDirectory = Join-Path $WorkspaceRoot "frontend"
    $viteCommand = Join-Path $frontendDirectory "node_modules\.bin\vite.cmd"
    if (-not (Test-Path -LiteralPath $viteCommand)) {
        Push-Location $frontendDirectory
        try {
            & npm.cmd ci
            if ($LASTEXITCODE -ne 0) {
                throw "npm ci failed with exit code $LASTEXITCODE."
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
    Add-StartedProcess -Kind "frontend" -Process $frontendProcess
}

Wait-ForUrl -Uri "http://127.0.0.1:5173" -Name "Frontend"

if (-not $NoClient) {
    $agentSolution = Join-Path $WorkspaceRoot "windows-agent\FactoryService.sln"
    & dotnet build $agentSolution --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "The Windows client build failed with exit code $LASTEXITCODE."
    }

    $serviceDirectory = Join-Path $WorkspaceRoot "windows-agent\src\FactoryService.Service\bin\Debug\net10.0-windows"
    $serviceExecutable = Join-Path $serviceDirectory "FactoryService.Service.exe"
    if (-not (Get-Process -Name "FactoryService.Service" -ErrorAction SilentlyContinue)) {
        $previousMachineData = $env:FACTORY_SERVICE_MACHINE_DATA
        $env:FACTORY_SERVICE_MACHINE_DATA = Join-Path $StateDirectory "client-service"
        try {
            $serviceProcess = Start-Process `
                -FilePath $serviceExecutable `
                -WorkingDirectory $serviceDirectory `
                -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $StateDirectory "client-service.out.log") `
                -RedirectStandardError (Join-Path $StateDirectory "client-service.err.log") `
                -PassThru
        }
        finally {
            $env:FACTORY_SERVICE_MACHINE_DATA = $previousMachineData
        }
        Add-StartedProcess -Kind "client-service" -Process $serviceProcess
    }

    $companionDirectory = Join-Path $WorkspaceRoot "windows-agent\src\FactoryService.Companion\bin\Debug\net10.0-windows"
    $companionExecutable = Join-Path $companionDirectory "FactoryService.Companion.exe"
    if (-not (Get-Process -Name "FactoryService.Companion" -ErrorAction SilentlyContinue)) {
        $previousApiBaseUrl = $env:FACTORY_SERVICE_API_BASE_URL
        $previousOperatorId = $env:FACTORY_SERVICE_OPERATOR_ID
        $env:FACTORY_SERVICE_API_BASE_URL = "http://localhost:5000"
        if (-not [string]::IsNullOrWhiteSpace($env:FACTORY_SERVICE_DEV_OPERATOR_ID)) {
            $env:FACTORY_SERVICE_OPERATOR_ID = $env:FACTORY_SERVICE_DEV_OPERATOR_ID
        }
        try {
            $companionProcess = Start-Process -FilePath $companionExecutable -WorkingDirectory $companionDirectory -PassThru
        }
        finally {
            $env:FACTORY_SERVICE_API_BASE_URL = $previousApiBaseUrl
            $env:FACTORY_SERVICE_OPERATOR_ID = $previousOperatorId
        }
        Add-StartedProcess -Kind "client-companion" -Process $companionProcess
    }
}

Write-Host "Factory Service is ready." -ForegroundColor Green
Write-Host "Admin:  http://localhost:5173/admin"
Write-Host "API:    http://localhost:5000/health"
if (-not $NoClient) {
    Write-Host "Client: Windows service (console mode) + Turmo tray companion"
}
Write-Host "Stop:   .\Stop-FactoryService.ps1"
