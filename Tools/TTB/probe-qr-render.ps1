param([Parameter(Mandatory)][string]$ApiArtifactDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifact=[IO.Path]::GetFullPath($ApiArtifactDirectory)
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($path in @($artifact,$output)){if(-not $path.StartsWith((Join-Path $root '.artifacts')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'QR QA inputs/outputs must stay in the owned artifact directory.'}}
if(Test-Path -LiteralPath $output){throw 'Preserve previous QR evidence. Use a new output directory.'}
New-Item -ItemType Directory -Path $output | Out-Null
# Load the accepted release assemblies in this child process; no API host, SQL connection or bank adapter starts.
$resolver=[Func[Runtime.Loader.AssemblyLoadContext,Reflection.AssemblyName,Reflection.Assembly]]{
  param($context,$name)
  $path=Join-Path $artifact ($name.Name+'.dll')
  if(Test-Path -LiteralPath $path){return $context.LoadFromAssemblyPath($path)}
  return $null
}.GetNewClosure()
[Runtime.Loader.AssemblyLoadContext]::Default.add_Resolving($resolver)
foreach($name in @('TtbCommerce.Core.dll','QRCoder.dll','TtbCommerce.Infrastructure.dll')){[Reflection.Assembly]::LoadFrom((Join-Path $artifact $name))|Out-Null}
$commerce=[TtbCommerce.Core.CommerceOptions]::new();$commerce.Environment=[TtbCommerce.Core.CommerceEnvironment]::Uat;$commerce.PaymentMode=[TtbCommerce.Core.PaymentMode]::Bank
$bank=[TtbCommerce.Core.TtbOptions]::new();$bank.BeneficiaryName='EXPIRED DOCUMENT FIXTURE - DO NOT PAY'
$service=[TtbCommerce.Infrastructure.QrService]::new($null,$null,$commerce,$bank,$null,[TimeProvider]::System)
$viewMethod=[TtbCommerce.Infrastructure.QrService].GetMethod('View',([Reflection.BindingFlags]::Instance -bor [Reflection.BindingFlags]::NonPublic))
if($null -eq $viewMethod){throw 'The accepted release renderer changed; inspect the public QR service before changing this probe.'}
$fixtures=@()
foreach($minor in @(100,1000,9900,10000,19978)){
  # This is the bank document's expired 2023 QRId and sample Biller. Never use the user's Biller or invent their Merchant ID.
  $input=[TtbCommerce.Core.ThaiQrInput]::new('010753700001716','ABCD1234567890XXXXXX','2222222222','ZXXXE231231235900001',[TtbCommerce.Core.Money]::new($minor),'ABC',$true)
  $payload=[TtbCommerce.Core.ThaiQr]::Build($input)
  $attempt=[TtbCommerce.Core.PaymentAttempt]::new();$attempt.Payload=$payload;$attempt.QrId=$input.QrId;$attempt.ExpectedMinor=$minor
  $attempt.ExpiresAt=[DateTimeOffset]::Parse('2023-12-31T16:59:00Z');$attempt.CreatedAt=[DateTimeOffset]::Parse('2023-12-31T16:49:00Z')
  $view=$viewMethod.Invoke($service,[object[]]@($attempt))
  $bytes=[Convert]::FromBase64String($view.ImageDataUrl.Substring('data:image/png;base64,'.Length))
  $name="expired-spec-$minor.png";[IO.File]::WriteAllBytes((Join-Path $output $name),$bytes)
  $fixtures+=@{file=$name;payload=$payload;amountMinor=$minor;expiresAt=$attempt.ExpiresAt.ToString('o');fixtureOnly=$true;actualReleaseRenderer=$true;sha256=[Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($bytes))}
}
$proof=@{generatedAt=[DateTimeOffset]::UtcNow.ToString('o');fixtures=$fixtures;bankRequestMade=$false;databaseConnected=$false;bankAccepted=$false;realBillerUsed=$false;assemblyHashes=@(Get-Item -LiteralPath (Join-Path $artifact 'TtbCommerce.Core.dll'),(Join-Path $artifact 'TtbCommerce.Infrastructure.dll'),(Join-Path $artifact 'QRCoder.dll')|ForEach-Object{@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})}
$proof|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $output 'fixtures.json') -Encoding utf8
Write-Output "Generated $($fixtures.Count) expired, non-payable document fixtures through the accepted release's actual QR PNG renderer. No SQL or bank request."
