param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath $OutputDirectory).Path
if(-not (Test-Path -LiteralPath $taskRoot -PathType Container)){throw 'OutputDirectory must be an existing directory.'}
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if($taskRoot.Equals($taskRepository,[StringComparison]::OrdinalIgnoreCase) -or $taskRoot.StartsWith($taskRepository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
  throw 'OutputDirectory must be outside the source repository.'
}
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path -LiteralPath $taskCompiler -PathType Leaf)){throw 'The .NET Framework x64 compiler is unavailable.'}
$taskRun=Join-Path $taskRoot ('host-state-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskExecutable=Join-Path $taskRun 'InputHostLifecycleTests.exe'
$taskArguments=@('/nologo','/utf8output','/target:exe','/platform:x64',('/out:'+$taskExecutable),(Join-Path $PSScriptRoot 'InputHostLifecycle.cs'),(Join-Path $PSScriptRoot 'InputHostLifecycleTests.cs'))
& $taskCompiler @taskArguments
if($LASTEXITCODE -ne 0){throw 'Input host lifecycle tests did not compile.'}
# A synchronous console program only: no Form, browser, input simulation or game.
$taskOutput=@(& $taskExecutable)
$taskExitCode=$LASTEXITCODE
$taskOutput | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskOutput | Where-Object {$_ -like 'PASS *'} | ForEach-Object {$_.Substring(5)})
$taskFailed=@($taskOutput | Where-Object {$_ -like 'FAIL *'} | ForEach-Object {$_.Substring(5)})
$taskReport=[pscustomobject]@{
  passed=($taskExitCode -eq 0 -and $taskFailed.Count -eq 0 -and $taskPassed.Count -gt 0)
  passedCount=$taskPassed.Count
  failedCount=$taskFailed.Count
  checks=$taskPassed
  failures=$taskFailed
  headless=$true
  gameLoaded=$false
}
$taskReport | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 4
if(-not $taskReport.passed){throw 'Input host lifecycle tests failed; see summary.json in the private output directory.'}
