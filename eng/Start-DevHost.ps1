# Starts ShiftIdentity.DevHost: an owned throwaway SQL database and the local identity host on top of it.
# The host prints its addresses and the synthetic accounts. Ctrl+C stops it; stopping drops the database, and this
# script then removes the SQL container it started.
#
# SQL: SHIFT_IDENTITY_TEST_SQL when it is set (a local or container server, as for the SQL tests). Otherwise a
# container from Start-IdentitySql.ps1 when Docker is available, and otherwise the local SQL Server Express
# instance (.\SQLEXPRESS), the SQL tests' own default.
#
# -Lan also binds the machine's private LAN address, so that a phone on the same network can open the device
# confirmation page from the QR code. -TunnelUrl is the HTTPS address of a tunnel to -Port (for example
# `devtunnel host -p 5288`), which the QR code then shows to phones. -AccessLifetimeSeconds shortens the access token
# (at most 900) to watch renewal. Visual Studio runs the same host from its DevHost launch profile.
param(
    [int]$Port = 0,
    [switch]$Lan,
    [string]$LanAddress,
    [string]$TunnelUrl,
    [int]$AccessLifetimeSeconds = 900,
    [string]$ArtifactsPath = (Join-Path ([IO.Path]::GetTempPath()) 'ShiftIdentityDevHost')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'ShiftIdentity.DevHost/ShiftIdentity.DevHost.csproj'
$build = @('--configuration', 'Debug', '--artifacts-path', $ArtifactsPath)
# A git worktree of this repo is not the folder the sibling framework repos reference. Build it against itself.
if ((Split-Path $root -Leaf) -ne 'ShiftIdentity') {
    $build += "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $PSScriptRoot 'WorktreeReferences.targets')"
}
$hostArguments = @('--port', $Port, '--access-lifetime', $AccessLifetimeSeconds)
if ($LanAddress) { $hostArguments += @('--lan-address', $LanAddress) }
elseif ($Lan) { $hostArguments += '--lan' }
if ($TunnelUrl) {
    if ($Port -eq 0) { throw '-TunnelUrl needs the -Port the tunnel forwards to.' }
    $hostArguments += @('--tunnel-url', $TunnelUrl)
}

# Separate artifacts keep the running host from locking the outputs an IDE builds.
& dotnet build $project @build
if ($LASTEXITCODE -ne 0) { throw 'ShiftIdentity.DevHost could not build.' }

$container = $null
$previousSql = $env:SHIFT_IDENTITY_TEST_SQL
try {
    if (-not $env:SHIFT_IDENTITY_TEST_SQL) {
        if (Get-Command docker -ErrorAction SilentlyContinue) {
            $sql = & (Join-Path $PSScriptRoot 'Start-IdentitySql.ps1') -PassThru | Select-Object -Last 1
            $container = $sql.Container
            $env:SHIFT_IDENTITY_TEST_SQL = $sql.Connection
        }
        else { Write-Host 'Docker is not available. Using the local SQL Server Express instance (.\SQLEXPRESS).' }
    }
    & dotnet run --project $project @build --no-build --no-restore --no-launch-profile -- @hostArguments
    if ($LASTEXITCODE -ne 0) { throw 'ShiftIdentity.DevHost stopped with an error.' }
}
finally {
    $env:SHIFT_IDENTITY_TEST_SQL = $previousSql
    if ($container) { & (Join-Path $PSScriptRoot 'Stop-IdentitySql.ps1') -Container $container }
}
