param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-bank-walk-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatBankWalkTests.exe'
$taskSources=@('FloatStillWaterGeometry.cs','FloatCastLaunchPlanner.cs','FloatBankWalk.cs','tests/FloatBankWalkTests.cs') |
  ForEach-Object {Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) $taskSources
if($LASTEXITCODE -ne 0){throw 'Bank walk checks did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskChecks=@($taskLines|Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskChecks.Count -eq 15);checks=$taskChecks;nativeExecuted=$false;saveReadOrWritten=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines | Out-Host;throw 'Bank walk checks failed.'}
$taskReport|ConvertTo-Json -Depth 3
