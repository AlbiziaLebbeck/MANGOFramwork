param([Parameter(Mandatory)][string]$PassingFullTrx)
$ErrorActionPreference='Stop'
$repositoryRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$runId=[Guid]::NewGuid().ToString('N')
$directory=Join-Path $repositoryRoot ('.artifacts/ci-gates/'+$runId)
[IO.Directory]::CreateDirectory($directory)|Out-Null
[IO.Directory]::CreateDirectory((Join-Path $repositoryRoot '.artifacts/ci'))|Out-Null
$reader=Join-Path $PSScriptRoot 'read-backend-evidence.ps1'
$original=Get-Content -LiteralPath $PassingFullTrx -Raw
$accepted=& $reader -TrxPath $PassingFullTrx
$checks=[Collections.Generic.List[object]]::new()
foreach($kind in @('skipped-row','missing-row','failed-summary','runner-error','unit-only')) {
 [xml]$copy=$original
 $counter=$copy.SelectSingleNode('//*[local-name()="Counters"]')
 $summary=$copy.SelectSingleNode('//*[local-name()="ResultSummary"]')
 $rows=@($copy.SelectNodes('//*[local-name()="UnitTestResult"]'))
 switch($kind) {
  'skipped-row' {$rows[0].SetAttribute('outcome','NotExecuted')}
  'missing-row' {$null=$rows[0].ParentNode.RemoveChild($rows[0])}
  'failed-summary' {$summary.SetAttribute('outcome','Failed')}
  'runner-error' {$counter.SetAttribute('error','1')}
  'unit-only' {
   $row=@($rows|Where-Object {$_.testName -eq 'TtbCommerce.Tests.DatabaseTests.Migrations_match_model_and_round_trip_on_empty_test_database'})[0]
   $null=$row.ParentNode.RemoveChild($row)
   foreach($attribute in @('total','executed','passed')){$counter.SetAttribute($attribute,([int]$counter.GetAttribute($attribute)-1).ToString())}
  }
 }
 $path=Join-Path $directory ($kind+'.trx');$copy.Save($path)
 $rejected=$false
 try{$null=& $reader -TrxPath $path}catch{$rejected=$true}
 if(-not $rejected){throw "CI accepted invalid test evidence: $kind"}
 $checks.Add([pscustomobject]@{case=$kind;rejected=$true})
}
$pwshExecutable=Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})
$savedConnection=$env:TTB_TEST_DATABASE;$savedFilter=$env:VSTEST_TESTCASEFILTER
try {
 foreach($kind in @('missing-database','wrong-database','non-loopback-database','filtered-suite')) {
  $before=@(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.artifacts/ci') -Directory|ForEach-Object {$_.Name})
  Remove-Item Env:VSTEST_TESTCASEFILTER -ErrorAction SilentlyContinue
  $env:TTB_TEST_DATABASE=switch($kind) {
   'missing-database' {''}
   'wrong-database' {'Host=127.0.0.1;Database=ttb_uat;Username=ci-guard;Password=synthetic-ci-only'}
   'non-loopback-database' {'Host=203.0.113.5;Database=ttb_development;Username=ci-guard;Password=synthetic-ci-only'}
   'filtered-suite' {$env:VSTEST_TESTCASEFILTER='FullyQualifiedName~FoundationTests';'Host=127.0.0.1;Database=ttb_development;Username=ci-guard;Password=synthetic-ci-only'}
  }
  $output=(& $pwshExecutable -NoLogo -NoProfile -File (Join-Path $PSScriptRoot 'check.ps1') -Component Backend -SkipDependencyRestore 2>&1)-join "`n"
  if($LASTEXITCODE -eq 0 -or $output.Contains('synthetic-ci-only')){throw 'CI backend preflight passed or exposed a connection credential.'}
  $created=@(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot '.artifacts/ci') -Directory|Where-Object {$_.Name -notin $before})
  if($created.Count -ne 1){throw 'CI preflight proof is missing or ambiguous.'}
  $report=Get-Content -LiteralPath (Join-Path $created[0].FullName 'result.json') -Raw|ConvertFrom-Json
  if($report.status -ne 'failed' -or $report.failedStage -ne 'preflight' -or $report.stages.Count -ne 0){throw 'CI did not reject before invoking build/test/network commands.'}
  $checks.Add([pscustomobject]@{case=$kind;rejectedBeforeCommands=$true;ciRunId=$report.runId})
 }
} finally {
 if($null -eq $savedConnection){Remove-Item Env:TTB_TEST_DATABASE -ErrorAction SilentlyContinue}else{$env:TTB_TEST_DATABASE=$savedConnection}
 if($null -eq $savedFilter){Remove-Item Env:VSTEST_TESTCASEFILTER -ErrorAction SilentlyContinue}else{$env:VSTEST_TESTCASEFILTER=$savedFilter}
}
$proof=@{verifiedAt=[DateTimeOffset]::UtcNow.ToString('o');passingFullTrxSha256=$accepted.sha256;
 passingFullTrxTests=$accepted.total;negativeChecks=$checks.ToArray();actualDatabaseOrBankConnectionsMade=$false}
$proof|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $directory 'result.json') -Encoding utf8
@{acceptedFullTrxTests=$accepted.total;negativeChecksPassed=$checks.Count;resultPath=(Join-Path $directory 'result.json')}|ConvertTo-Json
exit 0
