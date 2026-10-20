param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [ValidateSet('Uat','Local')][string]$ExpectedEnvironment='Uat',
    [switch]$KeepDrill
)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$python='C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$runId=[Guid]::NewGuid().ToString('N')
$namespace='ttb-restore-drill-'+$runId
$recovery=Join-Path $repositoryRoot ('.artifacts/ttb-recovery/drill-'+$runId)
$recipient=Get-Content -LiteralPath (Join-Path $repositoryRoot '.tools/local/uat-backup-recipient/recipient.json') -Raw | ConvertFrom-Json
$started=[DateTimeOffset]::UtcNow; $timer=[Diagnostics.Stopwatch]::StartNew()
& (Join-Path $PSScriptRoot 'decrypt-backup.ps1') -ArchivePath $ArchivePath -ExpectedSha256 $ExpectedSha256 -OutputDirectory $recovery
& $python (Join-Path $PSScriptRoot 'verify-backup-bundle.py') $recovery --environment $ExpectedEnvironment --recipient-sha256 $recipient.recipientSha256
if ($LASTEXITCODE -ne 0) { throw 'Local authenticated bundle verification failed; no namespace has been created.' }
. (Join-Path $PSScriptRoot 'recovery-ssh.ps1')
$existing=@(Invoke-RecoverySsh -Command "kubectl get namespace '$namespace' --ignore-not-found -o name" -LogStem (Join-Path $recovery 'namespace-check'))
if (($existing -join '').Trim()) { throw 'The generated drill namespace already exists; preserved.' }
$yaml=(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'restore-namespace.yaml') -Raw).Replace('__NAMESPACE__',$namespace).Replace('__DRILL_ID__',$runId)
$manifestPath=Join-Path $recovery 'restore-namespace.yaml'
[IO.File]::WriteAllText($manifestPath,$yaml,[Text.UTF8Encoding]::new($false))
Invoke-RecoverySsh -Command 'kubectl apply -f -' -InputFile $manifestPath -LogStem (Join-Path $recovery 'namespace-create') | Out-Null
Write-Output ('Created fresh egress-denied restore namespace: '+$namespace)
Invoke-RecoverySsh -Command "kubectl -n '$namespace' wait --for=condition=Ready pod/postgres --timeout=60s" -LogStem (Join-Path $recovery 'postgres-ready') | Out-Null
$dump=Join-Path $recovery 'verified/database.dump'
Invoke-RecoverySsh -Command "kubectl -n '$namespace' exec -i postgres -- pg_restore -U ttb_restore -d ttb_restore --exit-on-error --single-transaction --no-owner --no-acl --no-tablespaces" -InputFile $dump -LogStem (Join-Path $recovery 'pg-restore') | Out-Null
Write-Output 'PostgreSQL archive restored into the fresh disposable database; no API or worker was started.'
$sourceSql=Join-Path $recovery 'source-data.sql'; $restoredSql=Join-Path $recovery 'restored-data.sql'
Invoke-RecoverySsh -Command "kubectl -n '$namespace' exec -i postgres -- pg_restore --data-only --no-owner --no-acl --file=-" -InputFile $dump -OutputFile $sourceSql -LogStem (Join-Path $recovery 'source-export')
Invoke-RecoverySsh -Command "kubectl -n '$namespace' exec postgres -- pg_dump -U ttb_restore -d ttb_restore --data-only --no-owner --no-acl" -OutputFile $restoredSql -LogStem (Join-Path $recovery 'restored-export')
& $python (Join-Path $PSScriptRoot 'compare-restore-data.py') $sourceSql $restoredSql --output (Join-Path $recovery 'data-comparison.json')
if ($LASTEXITCODE -ne 0) { throw 'Restored data differs; preserve the isolated namespace and do not resume any worker.' }
$checksSql=@'
SELECT json_build_object(
  'environment',(SELECT "Environment" FROM ttb."Deployments" WHERE "Key"='singleton'),
  'unvalidatedForeignKeys',(SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname='ttb' AND c.contype='f' AND NOT c.convalidated),
  'tcpListener',current_setting('listen_addresses'));
'@
$checksPath=Join-Path $recovery 'checks.sql'; [IO.File]::WriteAllText($checksPath,$checksSql,[Text.UTF8Encoding]::new($false))
$checkOutput=Invoke-RecoverySsh -Command "kubectl -n '$namespace' exec -i postgres -- psql -X -At -U ttb_restore -d ttb_restore -v ON_ERROR_STOP=1" -InputFile $checksPath -LogStem (Join-Path $recovery 'database-checks')
$checks=$checkOutput.Trim() | ConvertFrom-Json
if ($checks.environment -ne $ExpectedEnvironment -or $checks.unvalidatedForeignKeys -ne 0 -or $checks.tcpListener -ne '') { throw 'Restored environment/constraints/socket-only boundary failed.' }
$refsSql=@'
SELECT COALESCE(json_agg(q),'[]'::json) FROM (
 SELECT 'assets' AS area,"Key" AS key,'.asset' AS extension,"Sha256" AS sha256,"ByteLength" AS bytes FROM ttb."DigitalAssets" WHERE "Ready"
 UNION ALL SELECT 'documents',"FileKey",'.pdf',"FileSha256","FileBytes" FROM ttb."Documents" WHERE "State"='Ready'
 UNION ALL SELECT 'reports',"FileKey",'.pgp',"EncryptedSha256","EncryptedBytes" FROM ttb."Reports"
 UNION ALL SELECT 'finance',"Key",'.proof',"Sha256","ByteLength" FROM ttb."FinanceEvidence"
) q;
'@
$refsSqlPath=Join-Path $recovery 'file-references.sql'; [IO.File]::WriteAllText($refsSqlPath,$refsSql,[Text.UTF8Encoding]::new($false))
$referencesPath=Join-Path $recovery 'file-references.json'
Invoke-RecoverySsh -Command "kubectl -n '$namespace' exec -i postgres -- psql -X -At -U ttb_restore -d ttb_restore -v ON_ERROR_STOP=1" -InputFile $refsSqlPath -OutputFile $referencesPath -LogStem (Join-Path $recovery 'file-references')
& $python (Join-Path $PSScriptRoot 'verify-restored-files.py') $recovery $referencesPath
if ($LASTEXITCODE -ne 0) { throw 'A database-referenced private object is not recoverable; preserve the failed drill and original archive.' }
$comparison=Get-Content -LiteralPath (Join-Path $recovery 'data-comparison.json') -Raw | ConvertFrom-Json
$files=Get-Content -LiteralPath (Join-Path $recovery 'referenced-files-verification.json') -Raw | ConvertFrom-Json
$timer.Stop()
$result=@{
    status='verified';rehearsalOnly=$true;startedAt=$started.ToString('o');completedAt=[DateTimeOffset]::UtcNow.ToString('o');elapsedSeconds=[Math]::Round($timer.Elapsed.TotalSeconds,3)
    environment=$ExpectedEnvironment;namespace=$namespace;archiveSha256=$ExpectedSha256.ToLowerInvariant();databaseRestored=$true;dataMatch=$comparison.dataMatch
    comparedTables=$comparison.tableCount;privateFilesVerified=$files.verifiedReferencedFiles;workersStarted=$false;tcpListener=$false;networkEgressAllowed=$false
    productionRtoCertified=$false;offServerRpoCertified=$false
}
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $recovery 'restore-drill.json') -Encoding utf8
Write-Output ('Isolated restore verified; elapsed seconds='+$result.elapsedSeconds+'; tables='+$result.comparedTables+'; private files='+$result.privateFilesVerified)
if (-not $KeepDrill) {
    $owner=Invoke-RecoverySsh -Command "kubectl get namespace '$namespace' -o jsonpath='{.metadata.labels.mangosgo\.com/ttb-restore-id}'" -LogStem (Join-Path $recovery 'cleanup-identity')
    if ($owner.Trim() -ne $runId -or $namespace -notmatch '^ttb-restore-drill-[a-f0-9]{32}$') { throw 'Restore is verified, but cleanup ownership did not match; namespace preserved.' }
    Invoke-RecoverySsh -Command "kubectl delete namespace '$namespace' --wait=false" -LogStem (Join-Path $recovery 'cleanup') | Out-Null
    Write-Output 'Requested cleanup of only the verified disposable namespace; source database, archive and local evidence remain intact.'
}
