param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$runDirectory = Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('float-angler-log-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$executable = Join-Path $runDirectory 'FloatAnglerLogTests.exe'
$sources = @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs',
    'FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs',
    'FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatGameLifecycle.cs','FloatGameOwner.cs','FloatJournalArchive.cs',
    'FloatCastPreparation.cs','FloatStillWaterGeometry.cs','BottomSimpleRig.cs','BottomEquipmentBridge.cs','BottomOwnedEquipment.cs',
    'FloatAnglerLog.cs','FloatBiteSimulation.cs','tests/BottomOwnedKitTests.cs','tests/FloatAnglerLogTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:FloatAnglerLogTests ('/out:' + $executable) $sources
if ($LASTEXITCODE -ne 0) {throw 'Angler log tests did not compile.'}
$lines = @(& $executable $runDirectory)
$exitCode = $LASTEXITCODE
$lines | Set-Content -LiteralPath (Join-Path $runDirectory 'test-output.txt') -Encoding utf8
$checks = @($lines | Where-Object {$_ -like 'PASS *'})
$report = [pscustomobject]@{passed=($exitCode -eq 0 -and $checks.Count -eq 17);checks=$checks;privateJournals=$true;personalSaveRead=$false;outputDirectory=$runDirectory}
$report | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
if (-not $report.passed) {$lines | Out-Host;throw 'Angler log checks failed.'}
$report | ConvertTo-Json -Depth 3
