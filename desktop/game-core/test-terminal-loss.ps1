param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-terminal-loss-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatTerminalLossTransactionTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+',('/out:'+$taskExecutable))
foreach($taskName in @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatGameLifecycle.cs','FloatGameOwner.cs','tests/FloatTerminalLossTransactionTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Terminal loss checks did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 34 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  schemaVersion=3;atomicTerminalLoss=$true;missingComponentsPersist=$true;privateFixtureFileIO=$true;scriptedWriteFaults=$true;knownUnwrittenRecoveryLimitationVerified=$true;
  automaticMigration=$false;equipmentDamageEnabled=$false;nativeExecuted=$false;webViewExecuted=$false;personalSaveRead=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Terminal loss checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
