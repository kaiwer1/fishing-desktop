param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('desktop-startup-tests-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'StartupTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'DesktopStartup.cs') (Join-Path $PSScriptRoot 'StartupTests.cs') (Join-Path $PSScriptRoot '../desktop-layer/DesktopLayer.cs')
if($LASTEXITCODE -ne 0){throw 'Startup policy checks did not compile.'}
$taskText=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskText | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
$taskCount=@($taskText | Where-Object {$_ -like 'PASS *'}).Count
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskCount -eq 24);checks=$taskCount;fakeDesktop=$true;shellMessageSent=$false;windowsCreated=$false}
$taskReport | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskText
if(-not $taskReport.passed){throw 'Startup policy checks failed.'}
