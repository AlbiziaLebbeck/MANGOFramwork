param([switch]$Postgres, [switch]$ExternalTunnel, [string]$Filter = '', [int]$HangTimeoutSeconds = 120)
$ErrorActionPreference = 'Stop'
$testTunnel = $null
if ($Postgres) {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
    $settingsPath = Join-Path $repositoryRoot '.tools\local\postgres-development.local.json'
    if (-not (Test-Path -LiteralPath $settingsPath)) { throw 'Run Infrastructure/TTB/dev/provision.ps1 and open the SSH database tunnel first.' }
    $env:TTB_TEST_DATABASE = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).ConnectionString
    if (-not $ExternalTunnel) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $testPort = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
        $listener.Stop()
        $logStem = Join-Path $repositoryRoot ('.tools\local\test-tunnel-' + [Guid]::NewGuid().ToString('N'))
        $testPodIp = (& ssh -o BatchMode=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=12 ntserver02 'kubectl -n ttb-commerce-dev get pods -l app=ttb-postgres -o jsonpath="{.items[0].status.podIP}"').Trim()
        $parsedAddress = $null
        if ($LASTEXITCODE -ne 0 -or -not [Net.IPAddress]::TryParse($testPodIp, [ref]$parsedAddress) -or -not $testPodIp.StartsWith('10.42.')) { throw 'Cannot locate the isolated development PostgreSQL pod.' }
        # Use SSH directly from the trusted node to its own pod. kubectl port-forward can abort all streams when DROP DATABASE FORCE closes one test connection.
        $testTunnel = Start-Process -FilePath ssh -ArgumentList @('-N', '-T', '-o', 'BatchMode=yes', '-o', 'StrictHostKeyChecking=yes', '-o', 'ConnectTimeout=12',
            '-o', 'ServerAliveInterval=15', '-o', 'ServerAliveCountMax=3', '-o', 'ExitOnForwardFailure=yes', '-L', "127.0.0.1:${testPort}:${testPodIp}:5432", 'ntserver02') -PassThru -WindowStyle Hidden -RedirectStandardOutput ($logStem + '.log') -RedirectStandardError ($logStem + '.err')
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        $ready = $false
        while ([DateTime]::UtcNow -lt $deadline -and -not $testTunnel.HasExited) {
            $probe = [Net.Sockets.TcpClient]::new()
            try { $probe.Connect('127.0.0.1', $testPort); $ready = $true } catch { $ready = $false } finally { $probe.Dispose() }
            if ($ready) { break }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ready) {
            if (-not $testTunnel.HasExited) { Stop-Process -Id $testTunnel.Id }
            throw "The isolated test database tunnel did not start. Inspect $logStem.err"
        }
        $env:TTB_TEST_DATABASE = $env:TTB_TEST_DATABASE -replace 'Port=\d+', "Port=$testPort"
    }
}
$testExitCode = 1
try {
    $testArguments = @('test', 'TtbCommerce.slnx', '--no-restore', '--nologo', '--verbosity', 'minimal', '--logger', 'trx;LogFilePrefix=commerce', '--results-directory', (Join-Path $PSScriptRoot '..\..\..\.artifacts\test-results'))
    if ($Filter) { $testArguments += @('--filter', $Filter) }
    if ($HangTimeoutSeconds -gt 0) { $testArguments += @('--blame-hang', '--blame-hang-timeout', ($HangTimeoutSeconds.ToString() + 's'), '--blame-hang-dump-type', 'mini') }
    & (Join-Path $PSScriptRoot 'dotnet.ps1') @testArguments
    $testExitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:\TTB_TEST_DATABASE -ErrorAction SilentlyContinue
    if ($null -ne $testTunnel -and -not $testTunnel.HasExited) { Stop-Process -Id $testTunnel.Id }
}
exit $testExitCode
