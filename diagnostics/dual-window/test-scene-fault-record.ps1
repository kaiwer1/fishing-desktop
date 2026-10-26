param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('scene-fault-record-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'SceneFaultRecordTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'SceneFaultRecord.cs') (Join-Path $PSScriptRoot 'SceneFaultRecordTests.cs')
if($LASTEXITCODE -ne 0){throw 'Scene fault envelope checks did not compile.'}
$taskOutput=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
if($taskExit -ne 0 -or @($taskOutput | Where-Object {$_ -like 'PASS *'}).Count -ne 18){throw 'Scene fault envelope checks failed.'}
$taskOutput
