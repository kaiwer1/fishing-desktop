param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('host-policy-native-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskManifest='/win32manifest:'+(Join-Path $PSScriptRoot '../dual-window/desktop.manifest')
$taskFixture=Join-Path $taskRun 'NativeEnvironmentFixture.exe'
& $taskCompiler /nologo /utf8output /target:winexe /platform:x64 /warnaserror+ /r:System.Drawing.dll /r:System.Windows.Forms.dll $taskManifest ('/out:'+$taskFixture) (Join-Path $PSScriptRoot 'NativeEnvironmentFixture.cs')
if($LASTEXITCODE -ne 0){throw 'Native policy fixture did not compile.'}
$taskExecutable=Join-Path $taskRun 'NativeEnvironmentTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ /r:System.Drawing.dll /r:System.Windows.Forms.dll $taskManifest ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'NativeEnvironment.cs') (Join-Path $PSScriptRoot 'RunGate.cs') (Join-Path $PSScriptRoot 'NativeEnvironmentTests.cs')
if($LASTEXITCODE -ne 0){throw 'Native policy tests did not compile.'}
$taskLines=@(& $taskExecutable $taskRun);$taskExit=$LASTEXITCODE
$taskLines | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskLines | Where-Object {$_ -like 'PASS *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -eq 3);passedCount=$taskPassed.Count;checks=$taskPassed;details=$taskLines;nativeForegroundSampled=$true;controlledOwnFixture=$true;lockOrPowerTransitionTested=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
if(-not $taskReport.passed){throw 'Native environment tests failed or were inconclusive.'}
