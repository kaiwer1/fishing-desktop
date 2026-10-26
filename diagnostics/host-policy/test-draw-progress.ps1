param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRun=Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('draw-progress-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'DrawProgressTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'DrawProgressGuard.cs') (Join-Path $PSScriptRoot 'DrawProgressTests.cs')
if($LASTEXITCODE -ne 0){throw 'Draw progress tests did not compile.'}
$taskOutput=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
$taskOutput
if($taskExit -ne 0 -or @($taskOutput | Where-Object {$_ -like 'PASS *'}).Count -ne 8){throw 'Draw progress checks failed.'}
