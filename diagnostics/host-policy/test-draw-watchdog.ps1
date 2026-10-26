param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRun=Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('draw-watchdog-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'DrawWatchdogTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'DrawWatchdog.cs') (Join-Path $PSScriptRoot 'DrawWatchdogTests.cs')
if($LASTEXITCODE -ne 0){throw 'Draw watchdog tests did not compile.'}
$taskOutput=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
$taskPassed=@($taskOutput | Where-Object {$_ -like 'PASS *'})
$taskFailed=@($taskOutput | Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 16 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;
    checks=$taskPassed;failures=$taskFailed;pureLogicOnly=$true;nativeWatchdogIntegrated=$false;gameLoaded=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
if(-not $taskReport.passed){throw 'Draw watchdog tests failed.'}
