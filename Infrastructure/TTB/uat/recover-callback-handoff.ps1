# Recover the existing UAT callback credentials for the operator. Never rotate
# the bank Secret or print its username/password to an agent/tool transcript.
param([switch]$ExportPrivateText)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$currentMode = & ssh -o BatchMode=yes -o StrictHostKeyChecking=yes ntserver02 'kubectl -n ttb-uat get configmap ttb-bank-acceptance -o jsonpath="{.data.Ttb__CallbackAuthentication}"'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the active UAT callback profile.' }
if (($currentMode -join '').Trim() -eq 'BankSourceIp') {
    Write-Output 'UAT uses the bank-confirmed source-IP profile. No username/password is required or exported. See Documentation/Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md.'
    return
}
$json = & ssh -o BatchMode=yes -o StrictHostKeyChecking=yes ntserver02 'kubectl -n ttb-uat get secret ttb-bank -o json'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the existing UAT bank profile.' }
$secret = ($json -join "`n") | ConvertFrom-Json
function Decode([string]$Name) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($secret.data.$Name)) }
$runId = $secret.metadata.labels.'mangosgo.com/profile-run'
if ($runId -notmatch '^[a-f0-9]{32}$' -or (Decode 'Ttb__MerchantId') -ne 'PYA' -or
    (Decode 'Ttb__BillerNo') -ne '075555900002101' -or (Decode 'Ttb__CallbackAuthentication') -ne 'Basic') {
    throw 'Unexpected callback profile; nothing was exported.'
}
$directory = Join-Path $root ".tools/bank-callback/Uat/$runId"
[IO.Directory]::CreateDirectory($directory) | Out-Null
if ((Get-Item -LiteralPath $directory).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Handoff directory cannot be a link.' }
$acl = [Security.AccessControl.DirectorySecurity]::new()
$user = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.SetOwner($user)
$acl.SetAccessRuleProtection($true, $false)
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($user, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $directory -AclObject $acl
$handoff = @{
    environment = 'Uat'; url = 'https://ttb-api-test.mangosgo.com/ttb/v1/credit-notification'
    authentication = 'HTTP Basic over HTTPS'; username = (Decode 'Ttb__CallbackUsername'); password = (Decode 'Ttb__CallbackPassword')
}
if ($handoff.username.Length -eq 0 -or $handoff.password.Length -lt 32) { throw 'The existing callback credentials are incomplete.' }
$bytes = [Text.Encoding]::UTF8.GetBytes(($handoff | ConvertTo-Json))
try {
    $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, [Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/Callback/v1'), [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.File]::WriteAllBytes((Join-Path $directory 'handoff.dpapi'), $protected)
    if ($ExportPrivateText) {
        $textPath = Join-Path $directory 'callback-uat-PRIVATE.txt'
        if (Test-Path -LiteralPath $textPath) {
            if ((Get-Item -LiteralPath $textPath).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { throw 'Private handoff cannot be a link.' }
        }
        [IO.File]::WriteAllBytes($textPath, $bytes)
        Write-Output "Private callback handoff for the operator: $textPath"
    }
    Write-Output "Encrypted callback handoff: $directory/handoff.dpapi"
    Write-Output 'Existing UAT credentials recovered; server credentials were not changed. No message was sent.'
} finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)
    $handoff=$null; $secret=$null; $json=$null
}
