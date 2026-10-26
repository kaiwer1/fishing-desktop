param(
  [Parameter(Mandatory=$true)][string]$SdkDirectory,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if($taskRoot.Equals($taskRepo,[StringComparison]::OrdinalIgnoreCase) -or $taskRoot.StartsWith($taskRepo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Runtime output must be outside the repository.'}
$taskRun=Join-Path $taskRoot ('webview-lifecycle-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskRun | Out-Null
$taskChecks=[Collections.Generic.List[object]]::new()
function Invoke-LifecycleProbe([string]$mode,[int]$expectedExit,[string]$reportName) {
  $taskSummary=Join-Path $taskRun 'lifecycle-summary.json'
  if(Test-Path -LiteralPath $taskSummary){Remove-Item -LiteralPath $taskSummary}
  $taskProcess=Start-Process -FilePath (Join-Path $taskRun 'InputProbeHost.exe') -ArgumentList $mode -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskOriginalFailure=$null
  $taskCleanupFailure=$null
  try {
    if(-not $taskProcess.WaitForExit(30000)){throw 'Lifecycle probe timed out.'}
    if($taskProcess.ExitCode -ne $expectedExit){throw ("Unexpected exit code: "+$taskProcess.ExitCode+"; private reports: "+$taskRun)}
    if($mode -eq '--lifecycle-probe') {
      $taskResult=Get-Content -LiteralPath $taskSummary -Raw | ConvertFrom-Json
      if(-not $taskResult.passed -or $taskResult.checks.Count -ne 6){throw 'Lifecycle checks did not complete.'}
      Copy-Item -LiteralPath $taskSummary -Destination (Join-Path $taskRun ($reportName+'.json'))
    } else {
      $taskFailure=Get-Content -LiteralPath (Join-Path $taskRun 'host-failure.json') -Raw | ConvertFrom-Json
      $taskTarget=Get-Content -LiteralPath (Join-Path $taskRun 'failure-probe-target.json') -Raw | ConvertFrom-Json
      if($taskFailure.reason -ne 'webview-process-failed' -or -not $taskTarget.dedicatedProfile){throw 'Expected owned WebView failure was not observed.'}
    }
    $taskChecks.Add([pscustomobject]@{check=$reportName;exitCode=$taskProcess.ExitCode;passed=$true})
  }
  catch { $taskOriginalFailure=$_ }
  finally {
    if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskProcess.WaitForExit(5000) | Out-Null}
    $taskProcess.Dispose()
    # Check cleanup on success AND failure, preserving the original test error.
    try {
      $taskTimer=[Diagnostics.Stopwatch]::StartNew()
      do {
        $taskRemaining=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Contains($taskRun) })
        if($taskRemaining.Count -eq 0){break}
        Start-Sleep -Milliseconds 100
      } while($taskTimer.ElapsedMilliseconds -lt 15000)
      if($taskRemaining.Count -gt 0){throw 'Dedicated WebView process has not exited; private profile left for inspection.'}
    }
    catch {
      $taskCleanupFailure=$_
      $_.Exception.Message | Set-Content -LiteralPath (Join-Path $taskRun 'cleanup-error.txt') -Encoding utf8
    }
  }
  if($taskOriginalFailure){throw $taskOriginalFailure}
  if($taskCleanupFailure){throw $taskCleanupFailure}
}
Invoke-LifecycleProbe '--lifecycle-probe' 0 'navigation-before-failure'
Invoke-LifecycleProbe '--failure-probe' 4 'owned-browser-failure-closes-host'
Invoke-LifecycleProbe '--lifecycle-probe' 0 'navigation-after-explicit-restart'
$taskReport=[pscustomobject]@{passed=$true;checks=$taskChecks.ToArray();visibleWindow=$false;nativeMouseTested=$false;gameLoaded=$false;remainingDedicatedWebViews=0}
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 6
