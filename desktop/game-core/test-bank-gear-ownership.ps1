param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('bank-gear-ownership-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'BankGearOwnershipTests.exe'
$taskSources=@('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs',
    'FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs',
    'FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatGameLifecycle.cs','FloatGameOwner.cs',
    'FloatCastPreparation.cs','FloatStillWaterGeometry.cs','BottomSimpleRig.cs','BottomEquipmentBridge.cs','BottomOwnedEquipment.cs',
    'tests/BottomOwnedKitTests.cs','tests/BankGearOwnershipTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:BankGearOwnershipTests ('/out:'+$taskExecutable) $taskSources
if($LASTEXITCODE -ne 0){throw 'Bank gear ownership tests did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskChecks=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskChecks.Count -eq 6);checks=$taskChecks;personalSaveRead=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Bank gear ownership tests failed.'}
$taskReport|ConvertTo-Json -Depth 3
