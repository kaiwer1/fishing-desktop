param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$private=Get-DualWindowPrivateRoot $OutputDirectory
$run=Join-Path $private ('lure-retrieve-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $run 'LureRetrieveTests.exe'
$sources=@('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatEquipmentOffers.cs','FloatEquipmentOffersCodec.cs',
    'FloatEquipmentCommerce.cs','FloatEquipmentArchive.cs','FloatCampWorkReward.cs','FloatEquipmentMigrationPolicy.cs','FloatEquipmentUpgradeCommand.cs',
    'FloatGameState.cs','FloatGameReducer.cs','FloatBiteSimulation.cs','FloatFightSimulation.cs','FloatStillWaterGeometry.cs',
    'FloatFlightSimulation.cs','FloatCastLaunchPlanner.cs','FloatFishingAtlas.cs','LureRetrieveSimulation.cs','tests/LureRetrieveTests.cs') |
    ForEach-Object {Join-Path $PSScriptRoot $_}
$build=@(& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ ('/out:'+$exe) $sources)
$buildExit=$LASTEXITCODE
$build | Set-Content -LiteralPath (Join-Path $run 'build-output.txt') -Encoding utf8
if($buildExit -ne 0){$build | Out-Host;throw 'Lure retrieval tests did not compile.'}
$lines=@(& $exe)
$testExit=$LASTEXITCODE
$lines | Set-Content -LiteralPath (Join-Path $run 'test-output.txt') -Encoding utf8
$passed=@($lines | Where-Object {$_ -like 'PASS *'})
$failed=@($lines | Where-Object {$_ -like 'FAIL *'})
$report=[pscustomobject]@{passed=($testExit -eq 0 -and $passed.Count -eq 17 -and $failed.Count -eq 0);passedCount=$passed.Count;
    checks=$passed;failures=$failed;actualFlight=$true;fixedSteps=$true;webViewExecuted=$false;personalSaveRead=$false;outputDirectory=$run}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8
if(-not $report.passed){$lines | Out-Host;throw 'Lure retrieval tests failed.'}
$report | ConvertTo-Json -Depth 4
