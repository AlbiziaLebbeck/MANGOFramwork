param([Parameter(Mandatory)][ValidatePattern('^[a-f0-9]{32}$')][string]$RunId)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$path=Join-Path $root ".tools/bank-callback/Uat/$RunId/handoff.dpapi"
$bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path),[Text.Encoding]::UTF8.GetBytes('MANGOsTTB/UAT/Callback/v1'),[Security.Cryptography.DataProtectionScope]::CurrentUser)
try { Write-Output ([Text.Encoding]::UTF8.GetString($bytes)) }
finally { [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes) }
# Run manually in a private terminal only. Share through the agreed secure bank channel, never Git or a public ticket.
