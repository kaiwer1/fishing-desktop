param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('output-environment-pure-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'OutputEnvironmentTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+','/r:System.Drawing.dll',('/out:'+$taskExecutable))
foreach($taskName in @('OutputEnvironmentCapture.cs','OutputEnvironmentTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
foreach($taskName in @('NativeEnvironment.cs','RunGate.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../host-policy/$taskName")}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Per-output environment pure checks did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines|Where-Object {$_ -like 'PASS *'});$taskFailed=@($taskLines|Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 66 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;checks=$taskPassed;failures=$taskFailed;
  syntheticRawQueriesOnly=$true;nativeQueriesExecuted=$false;physicalMultiMonitorAcceptance=$false;nativeFullscreenAcceptance=$false;hostIntegrated=$false;outputDirectory=$taskRun}
$taskReport|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines|Out-Host;throw 'Per-output environment pure checks failed.'}
$taskReport|ConvertTo-Json -Depth 4
