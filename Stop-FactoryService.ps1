[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$WorkspaceRoot = Split-Path -Parent $PSCommandPath
$stateFile = Join-Path $WorkspaceRoot ".dev\processes.json"

if (-not (Test-Path -LiteralPath $stateFile)) {
    Write-Host "No Factory Service processes recorded by the startup script."
    exit 0
}

$records = @(Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json)
foreach ($record in $records) {
    $process = Get-Process -Id $record.Pid -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        continue
    }

    $recordedStart = [DateTimeOffset]::Parse($record.StartedAtUtc).UtcDateTime
    $actualStart = $process.StartTime.ToUniversalTime()
    if ([Math]::Abs(($actualStart - $recordedStart).TotalSeconds) -gt 2) {
        Write-Warning "Skipped PID $($record.Pid): it has been reused by another process."
        continue
    }

    $descendants = [System.Collections.Generic.List[int]]::new()
    $pending = [System.Collections.Generic.Queue[int]]::new()
    $pending.Enqueue([int]$record.Pid)
    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        $children = Get-CimInstance Win32_Process -Filter "ParentProcessId = $parentId" -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            $childId = [int]$child.ProcessId
            $descendants.Add($childId)
            $pending.Enqueue($childId)
        }
    }

    for ($index = $descendants.Count - 1; $index -ge 0; $index--) {
        Stop-Process -Id $descendants[$index] -Force -ErrorAction SilentlyContinue
    }
    Stop-Process -Id $record.Pid -Force -ErrorAction SilentlyContinue
    Write-Host "Stopped $($record.Kind)."
}

Remove-Item -LiteralPath $stateFile -Force
