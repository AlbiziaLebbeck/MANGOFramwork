param([Parameter(Mandatory)][string]$TrxPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
[xml]$trx=Get-Content -LiteralPath $TrxPath -Raw
$counters=@($trx.SelectNodes('//*[local-name()="Counters"]'))
$summaries=@($trx.SelectNodes('//*[local-name()="ResultSummary"]'))
$rows=@($trx.SelectNodes('//*[local-name()="UnitTestResult"]'))
if($counters.Count -ne 1 -or $summaries.Count -ne 1 -or $summaries[0].outcome -ne 'Completed') {
 throw 'A unique completed backend test summary is required.'
}
$counter=$counters[0]
$total=[int]$counter.total
if($total -lt 1 -or [int]$counter.passed -ne $total -or [int]$counter.executed -ne $total -or $rows.Count -ne $total -or
 @($rows|Where-Object {$_.outcome -ne 'Passed'}).Count) {
 throw 'Every backend test must execute and pass; skipped/failed/missing results block publication.'
}
foreach($name in @('failed','error','timeout','aborted','inconclusive','notRunnable','notExecuted','disconnected','inProgress','pending')) {
 if([int]$counter.GetAttribute($name) -ne 0){throw 'An incomplete or failed backend test run cannot be published.'}
}
# Prove that this was an integration/PDF contract run rather than a green unit-only report.
foreach($required in @(
 'TtbCommerce.Tests.DatabaseTests.Migrations_match_model_and_round_trip_on_empty_test_database',
 'TtbCommerce.Tests.CallbackHttpTests.Approved_callback_requires_both_bank_transport_and_credentials_and_emits_bank_ACK_shape',
 'TtbCommerce.Tests.ReceiptTests.Actual_chromium_generates_Thai_receipt_with_many_lines_for_visual_QA'
)) {
 if(@($rows|Where-Object {$_.testName -eq $required}).Count -ne 1){throw 'A required PostgreSQL/callback/actual-Chromium result is missing or ambiguous.'}
}
[pscustomobject]@{total=$total;executed=[int]$counter.executed;passed=[int]$counter.passed;
 sha256=(Get-FileHash -LiteralPath $TrxPath -Algorithm SHA256).Hash.ToLowerInvariant()}
