param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('native-environment-queries-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'NativeEnvironmentQueryTests.exe'
$taskCommon=@('/nologo','/utf8output','/target:exe','/platform:x64','/warnaserror+','/r:System.Drawing.dll')
$taskPolicySources=@((Join-Path $PSScriptRoot 'NativeEnvironment.cs'),(Join-Path $PSScriptRoot 'RunGate.cs'))
$taskCompile=@(& $taskCompiler @taskCommon ('/out:'+$taskExecutable) @taskPolicySources (Join-Path $PSScriptRoot 'NativeEnvironmentQueryTests.cs'))
$taskCompile | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($LASTEXITCODE -ne 0){$taskCompile | Out-Host;throw 'Native query tests did not compile.'}
# Compile existing consumers without running their foreground fixture or any
# native query. This keeps the original public Capture(Rectangle) path checked.
$taskLegacy=Join-Path $taskRun 'NativeEnvironmentLegacyCompile.exe'
$taskLegacyCompile=@(& $taskCompiler @taskCommon /r:System.Windows.Forms.dll ('/out:'+$taskLegacy) @taskPolicySources (Join-Path $PSScriptRoot 'NativeEnvironmentTests.cs'))
$taskLegacyCompile | Set-Content -LiteralPath (Join-Path $taskRun 'legacy-build-output.txt') -Encoding utf8
if($LASTEXITCODE -ne 0){$taskLegacyCompile | Out-Host;throw 'Existing native test consumer did not compile.'}
$taskLines=@(& $taskExecutable);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskFailed=@($taskLines | Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 25 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;
    checks=$taskPassed;failures=$taskFailed;legacyConsumerCompiled=$true;nativeForegroundSampled=$false;actualSessionOrDisplayChanged=$false;
    fakeQueriesOnly=$true;fixtureStarted=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
if(-not $taskReport.passed){throw 'Native query tests failed.'}
