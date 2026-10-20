param()
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'The provisional UAT recovery custodian uses Windows CurrentUser DPAPI.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$keyRoot = Join-Path $repositoryRoot '.tools/local/uat-backup-recipient'
if (Test-Path -LiteralPath $keyRoot) { throw 'A UAT recipient already exists. Reuse it; rotation requires retaining every older recovery key.' }
New-Item -ItemType Directory -Path $keyRoot | Out-Null
$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl = Get-Acl -LiteralPath $keyRoot
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($account, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $keyRoot -AclObject $acl
$rsa = [Security.Cryptography.RSA]::Create(3072)
$certificate = $null
try {
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        ('CN=MANGOs TTB UAT Backup ' + [Guid]::NewGuid().ToString('N')), $rsa,
        [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment, $true))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddYears(5))
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $pfx = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password)
    $payload = [Text.Encoding]::UTF8.GetBytes((@{ format=1; pfx=[Convert]::ToBase64String($pfx); password=$password } | ConvertTo-Json -Compress))
    $entropy = [Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/BackupRecipient/v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect($payload, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $keyRoot 'recovery.dpapi'), $protected)
    [IO.File]::WriteAllText((Join-Path $keyRoot 'recipient.pem'), $certificate.ExportCertificatePem(), [Text.UTF8Encoding]::new($false))
    $fingerprint = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($certificate.RawData))
    @{ environment='Uat'; algorithm='CMS AES-256-GCM / RSA-3072 OAEP-SHA256'; recipientSha256=$fingerprint; createdAt=[DateTimeOffset]::UtcNow.ToString('o'); expiresAt=$certificate.NotAfter.ToUniversalTime().ToString('o'); keyCustodian='Windows CurrentUser DPAPI; production escrow not approved' } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $keyRoot 'recipient.json') -Encoding utf8
    Write-Output ('UAT recipient created. Public certificate SHA256: ' + $fingerprint)
    Write-Output 'Private material remains DPAPI-protected on this PC. No server/bank credential was created or transmitted.'
} finally {
    if ($null -ne $payload) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($payload) }
    if ($null -ne $pfx) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($pfx) }
    if ($null -ne $certificate) { $certificate.Dispose() }; $rsa.Dispose()
}
