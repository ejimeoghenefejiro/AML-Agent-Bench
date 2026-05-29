<#
.SYNOPSIS
    Run the AML-Agent-Bench reproducer suite in either local or Docker mode.

.DESCRIPTION
    One-button reproducer of the test sequence used to validate the framework:

      1. Build the solution
      2. Reference Oracle through the harness (no LLM, no Docker)
      3. Live agent through the harness against Task 006
      4. (Docker only) Python baseline submission through the harness

    Steps 3 and 4 hit the OpenAI API; total cost ~$0.005 per run.

.PARAMETER Mode
    'local'  - run the C# agent directly via dotnet run (no Docker).
    'docker' - build and run the agent as a Docker container.
    'both'   - run local first, then Docker (only if Docker is available).
    Default: local.

.PARAMETER Model
    Override BENCH_MODEL. Default: gpt-4o-mini.

.PARAMETER MaxSteps
    Override BENCH_MAX_STEPS. Default: 12.

.PARAMETER SkipPython
    Skip the Python baseline submission step (Docker mode only).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1
    powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode docker
    powershell -ExecutionPolicy Bypass -File scripts\test-bench.ps1 -Mode both
#>
[CmdletBinding()]
param(
    [ValidateSet('local', 'docker', 'both')]
    [string]$Mode = 'local',

    [string]$Model = 'gpt-4o-mini',

    [int]$MaxSteps = 12,

    [switch]$SkipPython
)

$ErrorActionPreference = 'Continue'
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $repoRoot

$summary = [ordered]@{}

function Invoke-Step {
    param([string]$Label, [scriptblock]$Body)

    Write-Host ''
    Write-Host '============================================================' -ForegroundColor Cyan
    Write-Host (' ' + $Label) -ForegroundColor Cyan
    Write-Host '============================================================' -ForegroundColor Cyan
    $start = Get-Date
    try {
        & $Body
        $rc = $LASTEXITCODE
        if ($null -eq $rc) { $rc = 0 }
    } catch {
        Write-Host ('[step] EXCEPTION: ' + $_) -ForegroundColor Red
        $rc = 1
    }
    $elapsed = (Get-Date) - $start
    $verdict = if ($rc -eq 0) { 'PASS' } else { 'FAIL' }
    $color = if ($rc -eq 0) { 'Green' } else { 'Red' }
    Write-Host ('[step] {0} ({1:N1}s)' -f $verdict, $elapsed.TotalSeconds) -ForegroundColor $color
    return @{ rc = $rc; elapsed = $elapsed.TotalSeconds }
}

function Test-DockerAvailable {
    try {
        docker info --format '{{.OSType}}' > $null 2>&1
        return $LASTEXITCODE -eq 0
    } catch { return $false }
}

# ---- step 1: build --------------------------------------------------------
$summary['1. build'] = Invoke-Step 'Step 1: dotnet build' {
    dotnet build "$repoRoot\AML-Agent-Bench.sln" -nologo -v minimal 2>&1 | Select-Object -Last 6
}

# ---- step 2: oracle -------------------------------------------------------
$summary['2. oracle'] = Invoke-Step 'Step 2: reference Oracle (no LLM, no Docker)' {
    dotnet run --project "$repoRoot\src\AmlAgent.Harness" --no-build -- --oracle --no-judge 2>&1 | Select-Object -Last 5
}

# ---- step 3: live agent (local and/or docker) -----------------------------
$dockerAvailable = Test-DockerAvailable
if ($Mode -eq 'docker' -and -not $dockerAvailable) {
    Write-Host '[error] -Mode docker requested but Docker is not available' -ForegroundColor Red
    exit 1
}

if ($Mode -eq 'local' -or $Mode -eq 'both') {
    $summary['3a. agent (local)'] = Invoke-Step ('Step 3a: C# agent (LOCAL, no Docker), ' + $Model) {
        dotnet run --project "$repoRoot\src\AmlAgent.Harness" --no-build -- `
            --agent csharp-sk `
            --task task-006-temporal-network-anomaly-detection `
            --model $Model `
            --max-steps $MaxSteps `
            --local 2>&1 | Select-Object -Last 30
    }
}

if ($Mode -eq 'docker' -or $Mode -eq 'both') {
    if (-not $dockerAvailable) {
        Write-Host '[warning] Docker not available; skipping Docker steps' -ForegroundColor Yellow
    } else {
        $summary['3b. agent (docker)'] = Invoke-Step ('Step 3b: C# agent (DOCKER), ' + $Model) {
            dotnet run --project "$repoRoot\src\AmlAgent.Harness" --no-build -- `
                --agent csharp-sk `
                --task task-006-temporal-network-anomaly-detection `
                --model $Model `
                --max-steps $MaxSteps 2>&1 | Select-Object -Last 30
        }

        if (-not $SkipPython) {
            $summary['4. python-baseline'] = Invoke-Step ('Step 4: Python baseline submission (DOCKER), ' + $Model) {
                dotnet run --project "$repoRoot\src\AmlAgent.Harness" --no-build -- `
                    --submission "$repoRoot\submissions\python-baseline" `
                    --task task-006-temporal-network-anomaly-detection `
                    --model $Model `
                    --max-steps $MaxSteps 2>&1 | Select-Object -Last 30
            }
        }
    }
}

# ---- summary --------------------------------------------------------------
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ' SUMMARY' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan
$anyFail = $false
foreach ($name in $summary.Keys) {
    $r = $summary[$name]
    if ($r.rc -eq 0) {
        $verdict = 'PASS'
        $color = 'Green'
    } else {
        $verdict = 'FAIL'
        $color = 'Red'
        $anyFail = $true
    }
    $line = '  {0,-22}  {1}  ({2:N1}s)' -f $name, $verdict, $r.elapsed
    Write-Host $line -ForegroundColor $color
}
Write-Host ''
if ($anyFail) {
    Write-Host 'OVERALL: FAIL  (at least one step did not return PASS)' -ForegroundColor Red
    exit 1
} else {
    Write-Host 'OVERALL: PASS  (all steps green)' -ForegroundColor Green
    exit 0
}
