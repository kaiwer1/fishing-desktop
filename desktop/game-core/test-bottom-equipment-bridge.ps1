param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('bottom-equipment-bridge-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'BottomEquipmentBridgeTests.exe'
$taskSources=@('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatStillWaterGeometry.cs','BottomSimpleRig.cs',
  'BottomEquipmentBridge.cs','tests/BottomEquipmentBridgeTests.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) $taskSources
if($LASTEXITCODE -ne 0){throw 'Bottom equipment bridge did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskChecks=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskChecks.Count -eq 17);checks=$taskChecks;
  ledgerBytesUnchanged=$true;privateFixture=$true;personalSaveRead=$false;nativeExecuted=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Bottom equipment bridge tests failed.'}
$taskReport|ConvertTo-Json -Depth 3
