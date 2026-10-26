param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('renderer-policy-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'RendererSuspenderTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'RendererSuspender.cs') (Join-Path $PSScriptRoot 'RendererSuspenderTests.cs')
if($LASTEXITCODE -ne 0){throw 'Renderer policy tests did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 14);passedCount=$taskPassed.Count;checks=$taskPassed;details=$taskLines;realWebViewTested=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
if(-not $taskReport.passed){throw 'Renderer policy tests failed.'}
