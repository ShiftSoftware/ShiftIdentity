$ErrorActionPreference = 'Stop'
# Microsoft Artifact Registry: 2022-CU25-ubuntu-22.04, verified 2026-09-06.
$image = 'mcr.microsoft.com/mssql/server@sha256:e07b9699a2b749969f19d86563ceeea22bd3a69f7f1db85a8d1ac4bdaf0c6f56'
$container = 'identity-tests-' + [Guid]::NewGuid().ToString('N')
$env:MSSQL_SA_PASSWORD = 'Test!1' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
$env:SQLCMDPASSWORD = $env:MSSQL_SA_PASSWORD
Write-Host "##vso[task.setvariable variable=IdentitySqlContainer]$container"
Write-Host "##vso[task.setvariable variable=IdentitySqlPassword;issecret=true]$env:MSSQL_SA_PASSWORD"
& docker run --detach --name $container --label "identity-test-owner=$container" --publish '127.0.0.1::1433' --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD $image
if ($LASTEXITCODE -ne 0) { throw 'Disposable SQL container could not start.' }
$ready = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    & docker exec --env SQLCMDPASSWORD $container /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -Q 'SELECT 1' *> $null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw 'Disposable SQL container did not become ready.' }
$binding = & docker port $container 1433/tcp
if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'SQL fixture must bind only to loopback.' }
$connection = "Server=127.0.0.1,$($Matches[1]);User ID=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=true;TrustServerCertificate=true;Connect Timeout=5"
Write-Host "##vso[task.setvariable variable=IdentitySqlConnection;issecret=true]$connection"
