[CmdletBinding()]
param(
    [ValidateSet('self', 'performance', 'integration', 'option', 'smart', 'ui', 'layout', 'size', 'record')]
    [string[]]$Suites = @('self', 'performance', 'integration', 'option', 'smart', 'ui', 'layout', 'size', 'record'),
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
$apps = @(Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts/build') -Filter '*.exe' -File)
if ($apps.Count -ne 1) { throw 'Expected exactly one built application.' }
$reports = Join-Path $repo 'artifacts/test-results'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$results = foreach ($suite in $Suites) {
    $report = Join-Path $reports ($suite + '.txt')
    $process = Start-Process -FilePath $apps[0].FullName -ArgumentList @("--$suite-test", ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    if (-not (Test-Path -LiteralPath $report)) { throw "No report for $suite; process exit code: $($process.ExitCode)." }
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $report | ForEach-Object { Write-Host $_ }; throw "$suite test failed." }
    $passed = @(Get-Content -LiteralPath $report | Where-Object { $_.StartsWith('PASS:') }).Count
    Write-Host "$suite : $passed assertions passed"
    [pscustomobject]@{ Suite = $suite; Passed = $passed }
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reports 'summary.json') -Encoding utf8
Write-Host ('Total assertions: ' + ($results | Measure-Object -Property Passed -Sum).Sum)
