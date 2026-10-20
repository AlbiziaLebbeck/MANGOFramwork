param()
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$recipientRoot = Join-Path $repositoryRoot '.tools/local/uat-backup-recipient'
$metadataPath = Join-Path $recipientRoot 'recipient.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$python = 'C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$openssl = 'C:/Program Files/Git/usr/bin/openssl.exe'
$source = (& $python (Join-Path $PSScriptRoot 'create_crypto_fixture.py') $metadataPath).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Synthetic crypto fixture preparation failed.' }
$run = [Guid]::NewGuid().ToString('N')
$archives = Join-Path $repositoryRoot '.artifacts/ttb-backups'
New-Item -ItemType Directory -Path $archives -Force | Out-Null
$archive = Join-Path $archives ('synthetic-crypto-' + $run + '.p7m')
& $openssl cms -encrypt -aes-256-gcm -binary -stream -outform DER -recip (Join-Path $recipientRoot 'recipient.pem') -keyopt rsa_padding_mode:oaep -keyopt rsa_oaep_md:sha256 -in (Join-Path $source 'bundle.tar.gz') -out $archive
if ($LASTEXITCODE -ne 0) { throw 'CMS encryption failed.' }
$recovery = Join-Path $repositoryRoot ('.artifacts/ttb-recovery/crypto-' + $run)
& (Join-Path $PSScriptRoot '../uat/decrypt-backup.ps1') -ArchivePath $archive -ExpectedSha256 (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -OutputDirectory $recovery
if ((Get-FileHash -LiteralPath (Join-Path $source 'bundle.tar.gz') -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $recovery 'bundle.tar.gz') -Algorithm SHA256).Hash) { throw 'CMS round-trip bytes changed.' }
& $python (Join-Path $PSScriptRoot '../uat/verify-backup-bundle.py') $recovery --environment Uat --recipient-sha256 $metadata.recipientSha256
if ($LASTEXITCODE -ne 0) { throw 'Decrypted synthetic bundle verification failed.' }
if (Get-ChildItem -LiteralPath $recovery -Filter '*.pfx') { throw 'Temporary recovery key was retained.' }
$tampered = Join-Path $archives ('synthetic-tampered-' + $run + '.p7m')
$bytes = [IO.File]::ReadAllBytes($archive); $bytes[$bytes.Length - 16] = $bytes[$bytes.Length - 16] -bxor 1
[IO.File]::WriteAllBytes($tampered, $bytes)
$rejected = Join-Path $repositoryRoot ('.artifacts/ttb-recovery/rejected-' + $run)
$failed = $false
try { & (Join-Path $PSScriptRoot '../uat/decrypt-backup.ps1') -ArchivePath $tampered -ExpectedSha256 (Get-FileHash -LiteralPath $tampered -Algorithm SHA256).Hash -OutputDirectory $rejected } catch { $failed = $true }
if (-not $failed -or (Test-Path -LiteralPath (Join-Path $rejected 'bundle.tar.gz'))) { throw 'Tampered CMS ciphertext was accepted or left authorized plaintext.' }
if (Get-ChildItem -LiteralPath $rejected -Filter '*.pfx') { throw 'A failed decryption retained temporary private material.' }
if (Test-Path -LiteralPath Env:TTB_UAT_RECOVERY_PFX_PASSWORD) { throw 'Recovery password environment was retained.' }
@{ status='passed'; test='Synthetic CMS AES-256-GCM / RSA-OAEP round-trip and tamper rejection'; privateKeyOnServer=$false; databaseRestored=$false; recipientSha256=$metadata.recipientSha256; archiveSha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $recovery 'crypto-test.json') -Encoding utf8
Write-Output 'Local crypto round-trip, byte/hash verification, tamper rejection and temporary-key cleanup passed. This was synthetic data, not a database restore.'
exit 0
