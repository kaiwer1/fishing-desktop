param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('failure-report-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskSource=Join-Path $taskRun 'GuardHolder.cs'
@'
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
static class GuardHolder {
    static int Main(string[] args) {
        if(args.Length!=1 || !Directory.Exists(args[0]))return 3;
        using(var guard=new HostInstanceGuard()) {
            if(!guard.IsOwner)return 2;
            File.WriteAllText(Path.Combine(args[0],"ready"),"ready");
            var clock=Stopwatch.StartNew();
            while(clock.ElapsedMilliseconds<30000 && !File.Exists(Path.Combine(args[0],"exit")))Thread.Sleep(50);
        }
        return 0;
    }
}
'@ | Set-Content -LiteralPath $taskSource -Encoding utf8
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'GuardHolder.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot '../input-window/HostInstanceGuard.cs') $taskSource
if($LASTEXITCODE -ne 0){throw 'Bounded guard holder did not compile.'}
$taskHolder=Start-Process -FilePath $taskExecutable -ArgumentList ('"'+$taskRun+'"') -WindowStyle Hidden -PassThru
$taskVerified=$false;$taskNormalExit=$false
try {
  $taskWait=[Diagnostics.Stopwatch]::StartNew()
  while(-not (Test-Path (Join-Path $taskRun 'ready')) -and -not $taskHolder.HasExited -and $taskWait.ElapsedMilliseconds -lt 5000){Start-Sleep -Milliseconds 50}
  if(-not (Test-Path (Join-Path $taskRun 'ready'))){throw 'Owned holder did not acquire guard; no other process is changed.'}
  $taskRejected=$false
  try{& (Join-Path $PSScriptRoot 'test-webviews.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskRun | Out-Null}catch{$taskRejected=$true}
  $taskBuilds=@(Get-ChildItem $taskRun -Directory -Filter 'dual-window-build-*')
  if(-not $taskRejected -or $taskBuilds.Count -ne 1){throw 'Second host did not reject the held guard as expected.'}
  $taskBuild=$taskBuilds[0].FullName
  $taskFailure=Get-Content (Join-Path $taskBuild 'failure-summary.json') -Raw | ConvertFrom-Json
  if($taskFailure.passed -or $taskFailure.exitCode -ne 2 -or $taskFailure.mode -ne '--self-test' -or -not $taskFailure.cleanupConfirmed -or $taskFailure.forcedCleanup -or $taskFailure.remainingDedicatedProcesses -ne 0){throw 'Expected rejection/cleanup evidence is missing.'}
  if((Test-Path (Join-Path $taskBuild 'counter.journal.jsonl')) -or (Test-Path (Join-Path $taskBuild 'private-profile'))){throw 'Rejected host opened owner state or browser data.'}
  $taskVerified=$true
} finally {
  try {
    if(-not $taskHolder.HasExited){[IO.File]::WriteAllText((Join-Path $taskRun 'exit'),'exit')}
    if(-not $taskHolder.WaitForExit(35000)){throw 'Bounded own guard holder did not exit; no broad cleanup is attempted.'}
    $taskNormalExit=$taskHolder.ExitCode -eq 0
  } finally {$taskHolder.Dispose()}
}
$taskReport=[pscustomobject]@{passed=($taskVerified -and $taskNormalExit);expectedSecondInstanceRejection=$taskVerified;failureCleanupConfirmed=$taskVerified;holderNormalExit=$taskNormalExit;nativeInputTested=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
if(-not $taskReport.passed){throw 'Failure report integration did not pass.'}
