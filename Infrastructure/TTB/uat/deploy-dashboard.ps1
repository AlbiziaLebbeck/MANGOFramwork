param(
 [Parameter(Mandatory)][string]$ArtifactDirectory,
 [Parameter(Mandatory)][ValidatePattern('^mangosgo:[a-f0-9-]{36}$')][string]$Subject,
 [ValidatePattern('^[a-z0-9][a-z0-9-]{2,40}$')][string]$ReleaseId='20261022-dashboard-v1'
)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$artifactRoot=Join-Path $repositoryRoot '.artifacts'
$artifact=[IO.Path]::GetFullPath($ArtifactDirectory)
if(-not $artifact.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Use a verified CI artifact inside .artifacts'}
foreach($name in @('TtbCommerce.Api','Dashboard/index.html','release-compatibility.json')){if(-not(Test-Path -LiteralPath (Join-Path $artifact $name))){throw "Missing artifact: $name"}}
$contract=(Get-Content -LiteralPath (Join-Path $artifact 'release-compatibility.json') -Raw|ConvertFrom-Json).readerContract
if($contract -lt 4){throw 'A contract 4 reader is required'}
$ciRoot=Split-Path $artifact
$ci=Get-Content -LiteralPath (Join-Path $ciRoot 'result.json') -Raw|ConvertFrom-Json
if($ci.status -ne 'passed' -or -not $ci.inputFilesUnchanged){throw 'The complete backend CI must pass'}
foreach($entry in $ci.artifactFiles){$file=Join-Path $repositoryRoot $entry.path;if(-not(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash.ToLowerInvariant() -ne $entry.sha256){throw 'CI artifact hash mismatch'}}
$ssh=@('-o','BatchMode=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=12','ntserver02')
$deployment=(& ssh @ssh 'kubectl -n ttb-uat get deployment ttb-commerce-api -o json')-join [Environment]::NewLine
if($LASTEXITCODE -ne 0){throw 'Cannot inspect the existing API deployment'}
$before=$deployment|ConvertFrom-Json
$index=-1
for($i=0;$i -lt $before.spec.template.spec.volumes.Count;$i++){if($before.spec.template.spec.volumes[$i].name -eq 'app'){$index=$i}}
if($index -lt 0 -or $before.spec.template.spec.containers.Count -ne 1){throw 'Unexpected deployment shape'}
if(@($before.spec.template.spec.containers[0].envFrom|Where-Object {$_.configMapRef.name -eq 'ttb-dashboard-access'}).Count){throw 'Dashboard is already configured; inspect and update the existing release deliberately'}
$floor=(& ssh @ssh 'cat /opt/ttb-commerce/uat/minimum-reader-contract')-join ''
if($LASTEXITCODE -ne 0 -or $floor -notmatch '^\d+$' -or [int]$floor -gt $contract){throw 'Incompatible reader floor'}
$archive=Join-Path $artifactRoot "$ReleaseId.tar.gz"
& tar -czf $archive -C $artifact .
if($LASTEXITCODE -ne 0){throw 'Cannot package Dashboard release'}
$hash=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
$remote="/opt/ttb-commerce/uat/releases/$ReleaseId"
& ssh @ssh "test ! -e '$remote' && mkdir -p '$remote/api'"
if($LASTEXITCODE -ne 0){throw 'Release already exists; inspect it instead of overwriting'}
& scp -o BatchMode=yes -o StrictHostKeyChecking=yes $archive "ntserver02:$remote/api.tar.gz"
if($LASTEXITCODE -ne 0){throw 'Artifact transfer failed'}
$prepare=@"
set -eu
printf '%s  %s\n' '$hash' '$remote/api.tar.gz' | sha256sum -c -
tar -xzf '$remote/api.tar.gz' -C '$remote/api'
chmod -R a+rX '$remote/api'
chmod a+rx '$remote/api/TtbCommerce.Api'
if [ -f '$remote/api/ReceiptProbe' ]; then chmod a+rx '$remote/api/ReceiptProbe'; fi
if [ -f '$remote/api/.playwright/node/linux-x64/node' ]; then chmod a+rx '$remote/api/.playwright/node/linux-x64/node'; fi
"@
$prepare|& ssh @ssh 'tr -d "\r" | bash -s'
if($LASTEXITCODE -ne 0){throw 'Release verification failed'}
$config=@{apiVersion='v1';kind='ConfigMap';metadata=@{name='ttb-dashboard-access';namespace='ttb-uat';labels=@{'app.kubernetes.io/part-of'='ttb-commerce'}};data=@{Dashboard__Enabled='true';Dashboard__Subjects__0=$Subject}}
$config|ConvertTo-Json -Depth 8|& ssh @ssh 'kubectl create -f -'
if($LASTEXITCODE -ne 0){throw 'Access config exists or could not be created; no deployment was changed'}
$patch=@(
 @{op='test';path='/metadata/uid';value=$before.metadata.uid},
 @{op='test';path='/metadata/resourceVersion';value=$before.metadata.resourceVersion},
 @{op='test';path="/spec/template/spec/volumes/$index/hostPath/path";value=$before.spec.template.spec.volumes[$index].hostPath.path},
 @{op='replace';path="/spec/template/spec/volumes/$index/hostPath/path";value="$remote/api"},
 @{op='add';path='/spec/template/spec/containers/0/envFrom/-';value=@{configMapRef=@{name='ttb-dashboard-access'}}}
)
$patch|ConvertTo-Json -Depth 12|& ssh @ssh 'kubectl -n ttb-uat patch deployment ttb-commerce-api --type=json --patch-file=/dev/stdin'
if($LASTEXITCODE -ne 0){throw 'Deployment changed concurrently. Inspect before retrying; keep the created release and config.'}
& ssh @ssh 'kubectl -n ttb-uat rollout status deployment/ttb-commerce-api --timeout=60s'
if($LASTEXITCODE -ne 0){throw 'Inspect this rollout before publishing the ingress'}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dashboard-ingress.yaml') -Raw|& ssh @ssh 'kubectl apply --dry-run=server -f -'
if($LASTEXITCODE -ne 0){throw 'Dashboard ingress validation failed'}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dashboard-ingress.yaml') -Raw|& ssh @ssh 'kubectl create -f -'
if($LASTEXITCODE -ne 0){throw 'Dashboard ingress exists or failed; inspect instead of overwriting'}
$evidence=@{releaseId=$ReleaseId;archiveSha256=$hash;apiUid=$before.metadata.uid;previousPath=$before.spec.template.spec.volumes[$index].hostPath.path;readerContract=$contract;ciRunId=$ci.runId;url='https://plus.mangosgo.com/mangoxttb/dashboard/';deployedAt=[DateTimeOffset]::UtcNow.ToString('o')}
$evidence|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $artifactRoot "$ReleaseId-deployment.json") -Encoding utf8
$evidence|ConvertTo-Json
