# Development QA only: existing synthetic Local database/files, no new DB role/grant or UAT/Production writes.
param()
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$directory=Join-Path $repositoryRoot ('.artifacts/ttb-recovery/local-source-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($directory) | Out-Null
$account=[Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl=Get-Acl -LiteralPath $directory; $acl.SetAccessRuleProtection($true,$false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($account,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $directory -AclObject $acl
. (Join-Path $PSScriptRoot 'recovery-ssh.ps1')
$sql=@'
SELECT json_build_object('environment',(SELECT "Environment" FROM ttb."Deployments" WHERE "Key"='singleton'),
 'orders',(SELECT count(*) FROM ttb."Orders"),'bankTransactions',(SELECT count(*) FROM ttb."BankTransactions"),
 'allocations',(SELECT count(*) FROM ttb."Allocations"),'readyDocuments',(SELECT count(*) FROM ttb."Documents" WHERE "State"='Ready'),
 'jobs',(SELECT count(*) FROM ttb."Jobs"));
'@
$query=Join-Path $directory 'fixture-counts.sql'; [IO.File]::WriteAllText($query,$sql,[Text.UTF8Encoding]::new($false))
$counts=Invoke-RecoverySsh -Command 'kubectl -n ttb-commerce-dev exec -i deployment/postgres -- psql -X -U ttb_development -d ttb_local_commerce -At -v ON_ERROR_STOP=1' -InputFile $query -LogStem (Join-Path $directory 'fixture-counts') | ConvertFrom-Json
if ($counts.environment -ne 'Local' -or $counts.orders -le 0 -or $counts.bankTransactions -le 0 -or $counts.allocations -le 0 -or $counts.readyDocuments -le 0 -or $counts.jobs -le 0) { throw 'The existing fixture must contain only Local data and actual synthetic commerce/document effects.' }
$counts | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'fixture-counts.json') -Encoding utf8
$dump=Join-Path $directory 'database.dump'
Invoke-RecoverySsh -Command 'kubectl -n ttb-commerce-dev exec deployment/postgres -- pg_dump -U ttb_development -d ttb_local_commerce --format=custom --no-owner --no-acl' -OutputFile $dump -LogStem (Join-Path $directory 'dump')
# --list may exit before consuming stdin, which can strand kubectl's input stream.
# Drain the complete controlled fixture into an owned temporary file before listing.
$temporary='/tmp/ttb-local-recovery-'+[Guid]::NewGuid().ToString('N')+'.dump'
$listing='kubectl -n ttb-commerce-dev exec -i deployment/postgres -- sh -c ''set -eu; trap "rm -f -- {0}" EXIT; cat > {0}; pg_restore --list {0}''' -f $temporary
Invoke-RecoverySsh -Command $listing -InputFile $dump -OutputFile (Join-Path $directory 'database.contents') -LogStem (Join-Path $directory 'contents')
$recipient=Get-Content -LiteralPath (Join-Path $repositoryRoot '.tools/local/uat-backup-recipient/recipient.json') -Raw | ConvertFrom-Json
& 'C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' (Join-Path $PSScriptRoot 'prepare-local-recovery.py') $directory --recipient-sha256 $recipient.recipientSha256
if ($LASTEXITCODE -ne 0) { throw 'Synthetic Local recovery bundle could not be prepared; source data was not modified.' }
$archive=Join-Path $directory 'synthetic-local.tar.gz.p7m'
& 'C:/Program Files/Git/usr/bin/openssl.exe' cms -encrypt -aes-256-gcm -binary -stream -outform DER -recip (Join-Path $repositoryRoot '.tools/local/uat-backup-recipient/recipient.pem') -keyopt rsa_padding_mode:oaep -keyopt rsa_oaep_md:sha256 -in (Join-Path $directory 'bundle.tar.gz') -out $archive
if ($LASTEXITCODE -ne 0) { throw 'Synthetic Local fixture encryption failed; do not use this archive.' }
Write-Output ('Encrypted synthetic Local fixture: '+$archive)
Write-Output ('SHA256: '+(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant())
