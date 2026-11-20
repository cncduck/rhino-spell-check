<#
.SYNOPSIS
    Builds the Rhino Spell Check plug-in and reports where to find the .rhp.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Test
    Also run the unit tests.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$Test
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# Rhino keeps the .rhp open while the plug-in is loaded, which makes the copy to
# bin\ fail with MSB3027. Say so up front rather than letting the build error out.
$rhino = Get-Process -Name 'Rhino' -ErrorAction SilentlyContinue
if ($rhino) {
    Write-Warning "Rhino is running (PID $($rhino.Id -join ', ')) and may be holding RhinoSpellCheck.rhp open."
    Write-Warning "If the build fails with MSB3027, close Rhino and run this again."
    Write-Host ""
}

Write-Host "Building RhinoSpellCheck ($Configuration)..." -ForegroundColor Cyan
dotnet build (Join-Path $root 'src\RhinoSpellCheck\RhinoSpellCheck.csproj') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Test) {
    Write-Host "`nRunning unit tests..." -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\RhinoSpellCheck.Tests\RhinoSpellCheck.Tests.csproj') -c $Configuration --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
}

$output = Join-Path $root "src\RhinoSpellCheck\bin\$Configuration"
$rhp = Join-Path $output 'RhinoSpellCheck.rhp'

if (-not (Test-Path $rhp)) { throw "Expected plug-in not found at $rhp" }

$dictionaries = Join-Path $output 'Dictionaries'
if (-not (Test-Path (Join-Path $dictionaries 'en_US.dic'))) {
    Write-Warning "Dictionaries folder is missing from the output; the plug-in will not be able to load a dictionary."
}

Write-Host "`nPlug-in built:" -ForegroundColor Green
Write-Host "  $rhp"
Write-Host ""
Write-Host "To install: drag that .rhp onto a running Rhino 8 window," -ForegroundColor Yellow
Write-Host "or use Tools > Options > Plug-ins > Install." -ForegroundColor Yellow
Write-Host ""
Write-Host "Keep the folder intact - the plug-in loads its dictionaries from" -ForegroundColor Yellow
Write-Host "the 'Dictionaries' folder sitting next to the .rhp." -ForegroundColor Yellow
Write-Host ""
Write-Host "Then enable the panel: Panels menu (or right-click a panel tab) > Spell Check."
