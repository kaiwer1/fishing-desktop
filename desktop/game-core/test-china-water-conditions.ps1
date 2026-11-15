param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$target=Get-DualWindowPrivateRoot $OutputDirectory
$run=Join-Path $target ('china-water-conditions-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $run 'ChinaWaterConditionsTests.exe'
$sources=@('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs',
    'FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs',
    'FloatGameState.cs','FloatGameReducer.cs','FloatBiteSimulation.cs','FloatStillWaterGeometry.cs','FloatFishingAtlas.cs',
    'ChinaWaterConditions.cs','tests/ChinaWaterConditionsTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
$build=@(& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ ('/out:'+$exe) $sources)
$buildResult=$LASTEXITCODE
$build|Set-Content -LiteralPath (Join-Path $run 'build-output.txt') -Encoding utf8
if($buildResult -ne 0){$build|Out-Host;throw 'Water condition tests did not compile.'}
$lines=@(& $exe)
$testResult=$LASTEXITCODE
$lines|Set-Content -LiteralPath (Join-Path $run 'test-output.txt') -Encoding utf8
$checks=@($lines|Where-Object {$_ -like 'PASS *'})
$failures=@($lines|Where-Object {$_ -like 'FAIL *'})
$report=[pscustomobject]@{passed=($testResult -eq 0 -and $checks.Count -eq 11 -and $failures.Count -eq 0);passedCount=$checks.Count;
    checks=$checks;failures=$failures;waters=3;localSpecies=7;twoDayCycle=$true;personalSaveRead=$false;nativeExecuted=$false;outputDirectory=$run}
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8
if(-not $report.passed){$lines|Out-Host;throw 'Water condition tests failed.'}
$report|ConvertTo-Json -Depth 4
