param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-line-break-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatLineBreakTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+',('/out:'+$taskExecutable),
  (Join-Path $PSScriptRoot 'FloatBiteSimulation.cs'),(Join-Path $PSScriptRoot 'FloatFightSimulation.cs'),(Join-Path $PSScriptRoot 'tests/FloatLineBreakTests.cs'))
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Float line break checks did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskObserved=@($taskLines|Where-Object {$_ -like 'OBSERVED *'}|ForEach-Object { $_.Substring(9)|ConvertFrom-Json })
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 19 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  originalLineBreakRules=$true;explicitLineBreakConfiguration=$true;physicalLoadTriggered=$true;fixedStepMilliseconds=50;hostActiveStepsOnly=$true;
  thresholdMilliN=5000;consecutiveExposureSteps=5;observations=$taskObserved;failureTicketAndAttemptGuarded=$true;integerLossEvidence=$true;
  equipmentLedgerMutated=$false;fishGranted=$false;inventoryMutated=$false;rf4RulesRestored=$false;
  nativeExecuted=$false;webViewExecuted=$false;saveReadOrWritten=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Float line break checks failed.'}
$taskReport|ConvertTo-Json -Depth 6
