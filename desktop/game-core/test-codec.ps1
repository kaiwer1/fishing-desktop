param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('float-journal-codec-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun|Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatJournalCodecTests.exe'
$taskBuild=@(& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'FloatEquipmentLedger.cs') (Join-Path $PSScriptRoot 'FloatEquipmentCodec.cs') (Join-Path $PSScriptRoot 'FloatGameState.cs') (Join-Path $PSScriptRoot 'FloatGameReducer.cs') (Join-Path $PSScriptRoot 'FloatJournalCodec.cs') (Join-Path $PSScriptRoot 'tests/FloatJournalCodecTests.cs'))
$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Strict float journal codec did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines|Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 23 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  binarySchema=1;maximumPayloadBytes=65536;pureCodecAndReducerOnly=$true;diskJournalImplemented=$false;hostOrPageIntegrated=$false;nativeExecuted=$false;personalSaveRead=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Strict float journal codec checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
