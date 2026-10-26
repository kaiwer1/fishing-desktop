param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('manual-record-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'ManualInputRecordTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ /r:System.Web.Extensions.dll ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'ManualInputRecord.cs') (Join-Path $PSScriptRoot 'ManualInputRecordTests.cs')
if($LASTEXITCODE -ne 0){throw 'Manual evidence checks did not compile.'}
$taskOutput=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
if($taskExit -ne 0 -or @($taskOutput | Where-Object {$_ -like 'PASS *'}).Count -ne 11){throw 'Manual evidence checks failed.'}
$taskOutput
