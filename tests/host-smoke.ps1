param([string]$Configuration = 'Debug', [string]$BuildDirectory = '', [string[]]$ExtraArguments = @(), [string]$WorkDirectory = '', [string]$ReportDirectory = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$buildRoot = Join-Path $projectRoot "bin\$Configuration\net10.0-windows"
if ($BuildDirectory) { $buildRoot = (Resolve-Path -LiteralPath $BuildDirectory).Path }
$testRoot = if ($WorkDirectory) { [IO.Path]::GetFullPath($WorkDirectory) } else { Join-Path ([IO.Path]::GetTempPath()) ('DeskWidgets-smoke-' + [guid]::NewGuid().ToString('N')) }
if (Test-Path -LiteralPath $testRoot) { throw "Test work directory already exists: $testRoot" }
New-Item -ItemType Directory -Path $testRoot | Out-Null
Copy-Item -Path (Join-Path $buildRoot '*') -Destination $testRoot -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'background-test.webm') -Destination (Join-Path $testRoot 'widgets\world-clock\background-test.webm')
# Test a private copy: the user's desktop layout is never test input or output.
$testConfig = Join-Path $testRoot 'config.json'
if (Test-Path -LiteralPath $testConfig) { Remove-Item -LiteralPath $testConfig }
foreach ($privateName in @('notes-history.json','notes-history.json.bak','notes-history.json.tmp')) { $privateCopy = Join-Path $testRoot $privateName; if (Test-Path -LiteralPath $privateCopy) { Remove-Item -LiteralPath $privateCopy } }
$oldLog = Join-Path $testRoot 'DeskWidgets.log'
if (Test-Path -LiteralPath $oldLog) { Remove-Item -LiteralPath $oldLog }
$process = Start-Process -FilePath (Join-Path $testRoot 'DeskWidgets.exe') -ArgumentList (@('--self-test','--isolated-test') + $ExtraArguments) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Smoke test timed out: $testRoot" }
$report = Join-Path $testRoot 'self-test.txt'
if (-not (Test-Path -LiteralPath $report)) { throw "No test report; exit $($process.ExitCode). Close any running DeskWidgets instance. Artifacts: $testRoot" }
Get-Content -LiteralPath $report
if ($process.ExitCode -ne 0) { throw "Host test failed; exit $($process.ExitCode). Artifacts: $testRoot" }
if (Test-Path -LiteralPath $oldLog) { Get-Content -LiteralPath $oldLog; throw "Host emitted errors. Artifacts: $testRoot" }
Write-Output "Artifacts: $testRoot"

if ($ReportDirectory) {
    if (Test-Path -LiteralPath $ReportDirectory) { throw "Report directory already exists: $ReportDirectory" }
    New-Item -ItemType Directory -Path $ReportDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $testRoot -File | Where-Object { $_.Name -eq 'self-test.txt' -or $_.Name -like '*-test.txt' -or $_.Name -like '*-native.json' -or $_.Name -like 'image-*.json' -or $_.Name -eq 'image-responses.txt' -or $_.Extension -eq '.png' } | Copy-Item -Destination $ReportDirectory
    Write-Output "Reports: $ReportDirectory"
}
