param(
    [Parameter(Mandatory)][string]$Project,
    # Optional. Without it the whole project runs; a project is the unit the pipeline selects.
    [string]$Filter,
    [Parameter(Mandatory)][string]$Suite,
    [Parameter(Mandatory)][string]$ResultsRoot,
    [switch]$NoRestore,
    # For a project a previous step already built in Release; saves the restore and build checks on every gate.
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$results = Join-Path $ResultsRoot ($Suite + '-' + [Guid]::NewGuid().ToString('N'))
$arguments = @('test', $Project, '--configuration', 'Release', '--logger', 'trx;LogFileName=results.trx', '--results-directory', $results, '-v:q', '-clp:ErrorsOnly')
if ($Filter) { $arguments += @('--filter', $Filter) }
if ($NoRestore) { $arguments += '--no-restore' }
if ($NoBuild) { $arguments += '--no-build' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "$Suite failed or could not start." }
[xml]$report = Get-Content -LiteralPath (Join-Path $results 'results.trx') -Raw
$counters = $report.TestRun.ResultSummary.Counters
if ([int]$counters.total -lt 1 -or [int]$counters.executed -ne [int]$counters.total -or [int]$counters.passed -ne [int]$counters.total) {
    throw "$Suite must run at least one test, with no failed or skipped tests."
}
Write-Host "$Suite passed: $($counters.passed) tests."
