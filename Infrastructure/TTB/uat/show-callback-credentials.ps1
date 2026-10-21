param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$currentMode = & ssh -o BatchMode=yes -o StrictHostKeyChecking=yes ntserver02 'kubectl -n ttb-uat get configmap ttb-bank-acceptance -o jsonpath="{.data.Ttb__CallbackAuthentication}"'
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the active UAT callback profile.' }
if (($currentMode -join '').Trim() -eq 'BankSourceIp') {
    Write-Output 'The archived Basic credentials are obsolete for UAT. TTB sends the documented common headers without Authorization. See Documentation/Provisioning/TTB_UAT_CALLBACK_HANDOFF_TH.md.'
    return
}
$path=Join-Path $root ".tools/bank-callback/Uat/$RunId/handoff.dpapi"
$bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path),[Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/Callback/v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
try { Write-Output ([Text.Encoding]::UTF8.GetString($bytes)) }
finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes) }
# Run manually in a private terminal only. Share through the agreed secure bank channel, never Git or a public ticket.
