param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepository=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if($taskOutput.Equals($taskRepository,[StringComparison]::OrdinalIgnoreCase) -or $taskOutput.StartsWith($taskRepository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
  throw 'Store test outputs must remain outside the public repository.'
}
$taskRun=Join-Path $taskOutput ('store-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExe=Join-Path $taskRun 'DurableCounterStoreTests.exe'
& $taskCompiler /nologo /target:exe /platform:x64 /warnaserror+ /r:System.Web.Extensions.dll "/out:$taskExe" (Join-Path $PSScriptRoot 'DurableCounterStore.cs') (Join-Path $PSScriptRoot 'DurableCounterStoreTests.cs')
if($LASTEXITCODE -ne 0){throw 'Store tests did not compile.'}
& $taskExe $taskRun
if($LASTEXITCODE -ne 0){throw 'Store tests failed.'}
$taskProcesses=[Collections.Generic.List[Diagnostics.Process]]::new()
$taskChecks=[Collections.Generic.List[string]]::new()
$taskJournal=Join-Path $taskRun 'process-journal.jsonl'

function Start-StoreProbe([string]$name) {
  $taskReady=Join-Path $taskRun ($name+'.ready.json')
  $taskRelease=Join-Path $taskRun ($name+'.release')
  $taskArguments=@('--hold-probe',('"'+$taskJournal+'"'),('"'+$taskReady+'"'),('"'+$taskRelease+'"'))
  $taskProcess=Start-Process -FilePath $taskExe -ArgumentList $taskArguments -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskProcesses.Add($taskProcess)
  return [pscustomobject]@{Process=$taskProcess;Ready=$taskReady;Release=$taskRelease}
}
function Read-StoreProbe($probe) {
  $taskWatch=[Diagnostics.Stopwatch]::StartNew()
  while($taskWatch.Elapsed.TotalSeconds -lt 5) {
    if(Test-Path -LiteralPath $probe.Ready) {
      try {
        $taskReport=Get-Content -LiteralPath $probe.Ready -Raw | ConvertFrom-Json
        if($taskReport.pid -ne $probe.Process.Id){throw 'Unexpected probe identity.'}
        return $taskReport
      } catch { }
    }
    if($probe.Process.HasExited){throw 'Store probe exited without a valid result.'}
    Start-Sleep -Milliseconds 25
  }
  throw 'Store probe readiness timed out.'
}
function Stop-StoreProbe($probe) {
  New-Item -ItemType File -Path $probe.Release | Out-Null
  if(-not $probe.Process.WaitForExit(5000) -or $probe.Process.ExitCode -ne 0){throw 'Store owner did not close normally.'}
}
try {
  $taskFirst=Start-StoreProbe 'first'
  $taskFirstResult=Read-StoreProbe $taskFirst
  if(-not $taskFirstResult.owner -or $taskFirstResult.value -ne 7 -or $taskFirstResult.sequence -ne 1 -or $taskFirstResult.duplicate){throw 'First owner failed.'}
  $taskChecks.Add('first process holds journal and commits durable counter')
  $taskDuplicate=Start-StoreProbe 'duplicate'
  $taskDuplicateResult=Read-StoreProbe $taskDuplicate
  if($taskDuplicateResult.owner -or -not $taskDuplicate.Process.WaitForExit(5000) -or $taskDuplicate.Process.ExitCode -ne 2){throw 'Concurrent writer was not rejected.'}
  $taskChecks.Add('second process cannot open same journal for writing')
  Stop-StoreProbe $taskFirst
  $taskNext=Start-StoreProbe 'normal-reopen'
  $taskNextResult=Read-StoreProbe $taskNext
  if(-not $taskNextResult.owner -or -not $taskNextResult.duplicate -or $taskNextResult.sequence -ne 1 -or $taskNextResult.value -ne 7){throw 'Normal reopen did not deduplicate.'}
  $taskChecks.Add('normal exit releases journal and repeat command is deduplicated')
  $taskNext.Process.Kill()
  if(-not $taskNext.Process.WaitForExit(5000)){throw 'Owned crash probe did not exit.'}
  $taskRecovered=Start-StoreProbe 'crash-reopen'
  $taskRecoveredResult=Read-StoreProbe $taskRecovered
  if(-not $taskRecoveredResult.owner -or -not $taskRecoveredResult.duplicate -or $taskRecoveredResult.sequence -ne 1 -or $taskRecoveredResult.value -ne 7){throw 'Crash recovery did not deduplicate.'}
  $taskChecks.Add('forced process termination releases lock without repeating committed change')
  Stop-StoreProbe $taskRecovered
  $taskStoreReport=Get-Content -LiteralPath (Join-Path $taskRun 'store-tests.json') -Raw | ConvertFrom-Json
  $taskReport=[pscustomobject]@{passed=$true;storeChecks=$taskStoreReport.count;processChecks=$taskChecks.ToArray();headless=$true;gameLoaded=$false}
  $taskReport | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
  $taskReport | ConvertTo-Json -Depth 4
}
finally {
  foreach($taskProcess in $taskProcesses) {
    if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskProcess.WaitForExit(5000) | Out-Null}
    $taskProcess.Dispose()
  }
}
