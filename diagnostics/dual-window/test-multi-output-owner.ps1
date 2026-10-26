param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('multi-output-owner-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'MultiOutputOwnerTests.exe'
$taskArgs=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+','/r:System.Web.Extensions.dll',('/out:'+$taskExecutable))
foreach($taskName in @('DualWindowOwner.cs','MultiOutputOwnerTests.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/RunGate.cs')
foreach($taskName in @('DurableCounterStore.cs','ViewAuthority.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../state-owner/$taskName")}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild | Out-Host;throw 'Multi-output owner checks did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskFailed=@($taskLines | Where-Object {$_ -like 'FAIL *'})
$taskResult=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 16 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;
    checks=$taskPassed;failures=$taskFailed;pureOwnerOnly=$true;webViewLoaded=$false;physicalMonitorsTested=$false;gameLoaded=$false;outputDirectory=$taskRun}
$taskResult | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskResult.passed){$taskLines | Out-Host;throw 'Multi-output owner checks failed.'}
$taskResult | ConvertTo-Json -Depth 4
