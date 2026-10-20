param([Parameter(Mandatory)][ValidatePattern('^ttb-uat-backup-[a-zA-Z0-9-]{1,100}$')][string]$JobName)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$sshArgs=@('-o','BatchMode=yes','-o','ConnectTimeout=12','-o','StrictHostKeyChecking=yes','ntserver02')
function Read-RemoteJson([string]$command) {
    $result=& ssh @sshArgs $command
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the owned UAT backup metadata.' }
    return ($result -join "`n") | ConvertFrom-Json
}
$job=Read-RemoteJson "kubectl -n ttb-uat get job '$JobName' -o json"
if (-not @($job.status.conditions | Where-Object { $_.type -eq 'Complete' -and $_.status -eq 'True' }).Count) { throw 'Only a successfully completed producer can be copied.' }
$logs=@(& ssh @sshArgs "kubectl -n ttb-uat logs 'job/$JobName' -c encrypt --tail=20")
if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the completed producer output.' }
$complete=@($logs | Where-Object { $_ -match '^TTB_UAT_BACKUP_COMPLETE file=(ttb-uat-[a-zA-Z0-9-]+\.tar\.gz\.p7m) recipient_sha256=([a-f0-9]{64})$' })
if ($complete.Count -ne 1) { throw 'Expected exactly one finalized encrypted archive.' }
$match=[regex]::Match($complete[0],'^TTB_UAT_BACKUP_COMPLETE file=(ttb-uat-[a-zA-Z0-9-]+\.tar\.gz\.p7m) recipient_sha256=([a-f0-9]{64})$')
$file=$match.Groups[1].Value; $recipientSha=$match.Groups[2].Value
$recipient=Get-Content -LiteralPath (Join-Path $repositoryRoot '.tools/local/uat-backup-recipient/recipient.json') -Raw | ConvertFrom-Json
if ($recipientSha -ne $recipient.recipientSha256) { throw 'The finalized archive uses an unexpected recovery recipient.' }
$pvc=Read-RemoteJson 'kubectl -n ttb-uat get pvc ttb-encrypted-backups -o json'
if ($pvc.status.phase -ne 'Bound' -or $pvc.spec.volumeName -notmatch '^pvc-[a-f0-9-]{36}$') { throw 'Backup PVC is not a known bound volume.' }
$pv=Read-RemoteJson ("kubectl get pv '"+$pvc.spec.volumeName+"' -o json")
$path=$pv.spec.local.path
if (-not $path) { $path=$pv.spec.hostPath.path }
if ($pv.spec.claimRef.namespace -ne 'ttb-uat' -or $pv.spec.claimRef.name -ne 'ttb-encrypted-backups' -or $pv.spec.claimRef.uid -ne $pvc.metadata.uid -or
    $path -notmatch '^/var/lib/rancher/k3s/storage/pvc-[a-f0-9-]{36}_ttb-uat_ttb-encrypted-backups$') { throw 'Backup volume ownership/path is unexpected; nothing copied.' }
$remoteFile=$path+'/'+$file
$declared=(& ssh @sshArgs "cat -- '$remoteFile.sha256'") -join ''
if ($LASTEXITCODE -ne 0 -or $declared -notmatch ('^([a-f0-9]{64})  '+[regex]::Escape($file)+'$')) { throw 'Final archive checksum metadata is invalid.' }
$expected=[regex]::Match($declared,'^[a-f0-9]{64}').Value
$measured=(& ssh @sshArgs "sha256sum -- '$remoteFile'") -join ''
if ($LASTEXITCODE -ne 0 -or $measured -ne ($expected+'  '+$remoteFile)) { throw 'Source ciphertext checksum does not match its finalization record.' }
$destination=Join-Path $repositoryRoot ('.artifacts/ttb-backups/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($destination) | Out-Null
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
& icacls $destination /inheritance:r /grant:r "*${sid}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict the off-server backup directory.' }
$localArchive=Join-Path $destination $file
. (Join-Path $PSScriptRoot 'recovery-ssh.ps1')
Invoke-RecoverySsh -Command "cat -- '$remoteFile'" -OutputFile $localArchive -LogStem (Join-Path $destination 'transfer')
if ((Get-FileHash -LiteralPath $localArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Off-server ciphertext checksum mismatch; preserve for investigation and do not decrypt.' }
$evidence=@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');environment='Uat';database='ttb_uat';job=$JobName;archive=$file;archiveSha256=$expected;recipientSha256=$recipientSha;bytes=(Get-Item -LiteralPath $localArchive).Length;sourceFinalized=$true;offServerCiphertextVerified=$true;databaseRestored=$false;continuousOffServerRpoCertified=$false}
$evidence | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'transfer.json') -Encoding utf8
Write-Output ('Verified off-server encrypted backup: '+$localArchive)
Write-Output ('SHA256: '+$expected)
