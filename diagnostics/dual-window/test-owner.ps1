param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskRun=Join-Path $taskRoot ('dual-window-owner-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskExecutable=Join-Path $taskRun 'DualWindowOwnerTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+','/r:System.Web.Extensions.dll',('/out:'+$taskExecutable))
foreach($taskName in @('ActivationHandshake.cs','DualWindowOwner.cs','DualWindowOwnerTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/RunGate.cs')
foreach($taskName in @('DurableCounterStore.cs','ViewAuthority.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../state-owner/$taskName")}
& $taskCompiler @taskArgs
if($LASTEXITCODE -ne 0){throw 'Dual-window owner tests did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines | Where-Object {$_ -like 'PASS *'} | ForEach-Object {$_.Substring(5)})
$taskFailed=@($taskLines | Where-Object {$_ -like 'FAIL *'} | ForEach-Object {$_.Substring(5)})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 14 -and $taskFailed.Count -eq 0);
  passedCount=$taskPassed.Count;failures=$taskFailed;checks=$taskPassed;headless=$true;webViewLoaded=$false;gameLoaded=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 5
if(-not $taskReport.passed){throw 'Dual-window owner tests failed.'}
