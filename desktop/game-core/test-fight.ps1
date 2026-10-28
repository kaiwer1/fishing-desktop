param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-fight-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatFightSimulationTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+',('/out:'+$taskExecutable),
  (Join-Path $PSScriptRoot 'FloatBiteSimulation.cs'),(Join-Path $PSScriptRoot 'FloatFightSimulation.cs'),(Join-Path $PSScriptRoot 'tests/FloatFightSimulationTests.cs'))
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Float fight checks did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 34 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  originalFixedStepFightModel=$true;fixedDepthRectangularWater=$true;hostActiveStepsOnly=$true;equipmentDamageImplemented=$false;ownedNetInventoryImplemented=$false;
  fishGranted=$false;inventoryMutated=$false;rf4RulesRestored=$false;nativeExecuted=$false;webViewExecuted=$false;saveReadOrWritten=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Float fight checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
