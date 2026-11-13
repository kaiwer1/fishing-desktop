param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-equipment-upgrade-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatEquipmentUpgradeTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+',('/out:'+$taskExecutable))
foreach($taskName in @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs','FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs','FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatJournalArchive.cs','tests/FloatEquipmentUpgradeTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Equipment upgrade checks did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 16 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  schemaVersion=3;explicitSchema2Upgrade=$true;sourcePrefixProof=$true;legacyAssetIdentityPreserved=$true;privateFixtureFileIO=$true;scriptedWriteFaults=$true;
  automaticMigration=$false;candidateServiceTested=$false;personalSaveRead=$false;realDiskFailure=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Equipment upgrade checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
