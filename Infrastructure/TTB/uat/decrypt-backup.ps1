param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Use the Windows CurrentUser UAT recovery custodian.' }
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.artifacts'))
$archive = [IO.Path]::GetFullPath($ArchivePath)
$recoveryRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot 'ttb-recovery'))
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $archive.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($archive) -ne '.p7m') { throw 'Encrypted input must be a workspace artifact with .p7m extension.' }
if (-not $output.StartsWith($recoveryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Use a new directory under .artifacts/ttb-recovery; existing recovery data is never overwritten.' }
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $ExpectedSha256) { throw 'Ciphertext fingerprint does not match the independently recorded transfer hash.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$account = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$acl = Get-Acl -LiteralPath $output; $acl.SetAccessRuleProtection($true,$false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($account,'FullControl','ContainerInherit,ObjectInherit','None','Allow'))
Set-Acl -LiteralPath $output -AclObject $acl
$recipientRoot = Join-Path $repositoryRoot '.tools/local/uat-backup-recipient'
$public = Join-Path $recipientRoot 'recipient.pem'
$protected = [IO.File]::ReadAllBytes((Join-Path $recipientRoot 'recovery.dpapi'))
$payload = [Security.Cryptography.ProtectedData]::Unprotect($protected, [Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/BackupRecipient/v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
$private = Join-Path $output ('recipient-' + [Guid]::NewGuid().ToString('N') + '.pfx')
$plaintext = Join-Path $output 'bundle.tar.gz'
$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) { $openssl = 'C:/Program Files/Git/usr/bin/openssl.exe' }
if (-not (Test-Path -LiteralPath $openssl)) { throw 'The installed OpenSSL CLI is required.' }
try {
    $key = [Text.Encoding]::UTF8.GetString($payload) | ConvertFrom-Json
    if ($key.format -ne 1) { throw 'Unsupported UAT recovery key format.' }
    [IO.File]::WriteAllBytes($private, [Convert]::FromBase64String($key.pfx))
    $env:TTB_UAT_RECOVERY_PFX_PASSWORD = $key.password
    & $openssl cms -decrypt -binary -inform DER -in $archive -recip $public -inkey $private -keyform P12 -passin env:TTB_UAT_RECOVERY_PFX_PASSWORD -out $plaintext
    if ($LASTEXITCODE -ne 0) { if (Test-Path -LiteralPath $plaintext) { Remove-Item -LiteralPath $plaintext -Force }; throw 'Authenticated decryption failed. No plaintext is authorized for extraction or restore.' }
    if (-not (Test-Path -LiteralPath $plaintext) -or (Get-Item -LiteralPath $plaintext).Length -lt 1) { throw 'Decryption produced an empty bundle.' }
    Write-Output ('Authenticated UAT backup decrypted into restricted directory: ' + $output)
    Write-Output 'Archive still requires safe member/hash/environment/reader verification before any isolated database restore.'
} finally {
    Remove-Item -LiteralPath Env:TTB_UAT_RECOVERY_PFX_PASSWORD -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $private) { Remove-Item -LiteralPath $private -Force }
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($payload)
    $key = $null
}
