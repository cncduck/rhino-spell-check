<#
.SYNOPSIS
    Runs every test suite and reports one set of totals.

.DESCRIPTION
    Three suites, each covering something the others cannot:

      unit         Core logic on a normal runner. Fast, no Rhino.
      integration  Document-facing behaviour against real Rhino geometry,
                   headless: scanning, corrections, sheets, details and where
                   navigation takes the user.
      panel        The Eto panel driven inside a hidden Rhino. Every bug the user
                   reported by hand before this suite existed was panel
                   behaviour that the other suites passed straight through.

    Build Debug throughout: a running Rhino holds bin\Release open, and a failed
    Release copy leaves the previous binary in place, so the suites would silently
    test stale code.

.PARAMETER Skip
    Suites to skip: unit, integration, panel.
#>
[CmdletBinding()]
param(
    [ValidateSet('unit', 'integration', 'panel')]
    [string[]]$Skip = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$results = @()
function Add-Result([string]$suite, [string]$outcome, [string]$detail) {
    $script:results += ,@{ Suite = $suite; Outcome = $outcome; Detail = $detail }
}

# --- unit ----------------------------------------------------------------------
if ($Skip -notcontains 'unit') {
    Write-Host "`n=== unit ===" -ForegroundColor Cyan
    $out = dotnet test (Join-Path $root 'tests\RhinoSpellCheck.Tests\RhinoSpellCheck.Tests.csproj') `
        -c Debug --nologo -v q 2>&1
    $line = $out | Select-String -Pattern 'Passed!|Failed!' | Select-Object -First 1
    Write-Host $line
    if ($line -match 'Passed:\s*(\d+)') {
        Add-Result 'unit' $(if ($line -match 'Failed!') { 'FAIL' } else { 'pass' }) "$($matches[1]) tests"
    }
    else { Add-Result 'unit' 'FAIL' 'no result line' }
}

# --- integration ---------------------------------------------------------------
if ($Skip -notcontains 'integration') {
    Write-Host "`n=== integration ===" -ForegroundColor Cyan
    dotnet build (Join-Path $root 'tests\RhinoSpellCheck.Harness\RhinoSpellCheck.Harness.csproj') `
        -c Debug --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Add-Result 'integration' 'FAIL' 'build failed'
    }
    else {
        $exe = Join-Path $root 'tests\RhinoSpellCheck.Harness\bin\Debug\net48\RhinoSpellCheck.Harness.exe'
        $out = & $exe 2>&1
        $out | Select-String -Pattern '^\s*FAIL\s' -CaseSensitive | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        $line = $out | Select-String -Pattern 'passed:\s*\d+' | Select-Object -Last 1
        Write-Host $line
        if ($line -match 'passed:\s*(\d+)\s+failed:\s*(\d+)') {
            Add-Result 'integration' $(if ([int]$matches[2] -gt 0) { 'FAIL' } else { 'pass' }) "$($matches[1]) checks"
        }
        else { Add-Result 'integration' 'FAIL' 'no result line' }
    }
}

# --- panel ---------------------------------------------------------------------
if ($Skip -notcontains 'panel') {
    Write-Host "`n=== panel ===" -ForegroundColor Cyan
    dotnet build (Join-Path $root 'tests\RhinoSpellCheck.Panel\RhinoSpellCheck.Panel.csproj') `
        -c Debug --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Add-Result 'panel' 'FAIL' 'build failed'
    }
    else {
        # Run with a time limit: this step has been seen to stall once, with
        # the child sitting idle, and a hung suite is worse than a failed one.
        $exe = Join-Path $root 'tests\RhinoSpellCheck.Panel\bin\Debug\net48\RhinoSpellCheck.Panel.exe'
        $job = Start-Job { param($e) & $e 2>&1 } -ArgumentList $exe
        if (Wait-Job $job -Timeout 300) {
            $out = Receive-Job $job
        }
        else {
            Stop-Job $job
            Get-Process 'RhinoSpellCheck.Panel' -ErrorAction SilentlyContinue | Stop-Process -Force
            $out = @('timed out after 300s')
        }
        Remove-Job $job -Force

        $out | Select-String -Pattern '^\s*(FAIL|WARNING)|timed out' -CaseSensitive | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        $line = $out | Select-String -Pattern 'passed:\s*\d+' | Select-Object -Last 1
        Write-Host $line
        if ($line -match 'passed:\s*(\d+)\s+failed:\s*(\d+)') {
            Add-Result 'panel' $(if ([int]$matches[2] -gt 0) { 'FAIL' } else { 'pass' }) "$($matches[1]) checks"
        }
        else { Add-Result 'panel' 'FAIL' 'no result line (timed out, or is the plug-in installed?)' }
    }
}

# --- summary -------------------------------------------------------------------
Write-Host "`n================ summary ================" -ForegroundColor Cyan
foreach ($r in $results) {
    $colour = if ($r.Outcome -eq 'pass') { 'Green' } else { 'Red' }
    Write-Host ("  {0,-13} {1,-6} {2}" -f $r.Suite, $r.Outcome, $r.Detail) -ForegroundColor $colour
}

$failed = @($results | Where-Object { $_.Outcome -ne 'pass' }).Count
if ($failed -gt 0) {
    Write-Host "`n$failed suite(s) failed." -ForegroundColor Red
    exit 1
}

Write-Host "`nAll suites passed." -ForegroundColor Green
