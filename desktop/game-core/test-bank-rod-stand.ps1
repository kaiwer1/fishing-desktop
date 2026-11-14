param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('bank-rod-stand-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'BankRodStandTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'BankRodStand.cs') (Join-Path $PSScriptRoot 'tests/BankRodStandTests.cs')
if($LASTEXITCODE -ne 0){throw 'Bank rod stand checks did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'output.txt') -Encoding utf8
$taskChecks=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskChecks.Count -eq 15);checks=$taskChecks;inMemoryOwnershipOnly=$true;concurrentFishingImplemented=$false;nativeExecuted=$false;saveReadOrWritten=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
if(-not $taskReport.passed){$taskLines | Out-Host;throw 'Bank rod stand checks failed.'}
$taskReport | ConvertTo-Json -Depth 3
