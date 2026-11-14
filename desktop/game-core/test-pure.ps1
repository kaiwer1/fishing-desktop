param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-game-core-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatGameReducerTests.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/warnaserror+',('/out:'+$taskExecutable))
foreach($taskName in @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs',
    'FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs',
    'FloatGameState.cs','FloatGameReducer.cs','tests/FloatGameReducerTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskBuild=@(& $taskCompiler @taskArgs)
$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Float reducer candidate did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 32 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  pureReducerCandidate=$true;realPersistence=$false;hostIntegrated=$false;pageProtocolImplemented=$false;nativeExecuted=$false;personalSaveRead=$false;fixtureRulesOnly=$true;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Float reducer candidate checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
