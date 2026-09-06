param([string]$Container)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Container) -or $Container.StartsWith('$(')) { return }
if ($Container -notmatch '^identity-tests-[a-f0-9]{32}$') { throw 'Unsafe SQL fixture container name.' }
$owner = & docker inspect --format '{{ index .Config.Labels "identity-test-owner" }}' $Container
if ($LASTEXITCODE -ne 0) { throw 'SQL fixture ownership could not be verified.' }
if ($owner -ne $Container) { throw 'SQL fixture ownership mismatch; refusing cleanup.' }
& docker rm --force $Container
if ($LASTEXITCODE -ne 0) { throw 'Owned SQL fixture cleanup failed.' }
