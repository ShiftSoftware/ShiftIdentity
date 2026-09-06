param(
    [Parameter(Mandatory)][string]$Project,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][string]$Suite,
    [Parameter(Mandatory)][string]$ResultsRoot,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$results = Join-Path $ResultsRoot ($Suite + '-' + [Guid]::NewGuid().ToString('N'))
$arguments = @('test', $Project, '--configuration', 'Release', '--filter', $Filter, '--logger', 'trx;LogFileName=results.trx', '--results-directory', $results, '-v:q', '-clp:ErrorsOnly')
if ($NoRestore) { $arguments += '--no-restore' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "$Suite failed or could not start." }
[xml]$report = Get-Content -LiteralPath (Join-Path $results 'results.trx') -Raw
$counters = $report.TestRun.ResultSummary.Counters
if ([int]$counters.total -lt 1 -or [int]$counters.executed -ne [int]$counters.total -or [int]$counters.passed -ne [int]$counters.total) {
    throw "$Suite must run at least one test, with no failed or skipped tests."
}
Write-Host "$Suite passed: $($counters.passed) tests."
