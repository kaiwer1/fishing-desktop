param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
$taskExecutable=[IO.Path]::GetFullPath($taskBuild.executable)
$taskRun=$taskBuild.outputDirectory
$taskProcess=$null;$taskStartedTicks=0L;$taskForced=$false;$taskExited=$false;$taskIdentityMatched=$false;$taskExit=$null
function Test-EnvironmentOwnProcess($Process,[string]$ExpectedPath,[long]$ExpectedStart) {
  if($null -eq $Process){return $false}
  try{
    $Process.Refresh()
    return (-not $Process.HasExited -and [IO.Path]::GetFullPath($Process.MainModule.FileName).Equals($ExpectedPath,[StringComparison]::OrdinalIgnoreCase) -and $Process.StartTime.ToUniversalTime().Ticks -eq $ExpectedStart)
  }catch{return $false}
}
try{
  $taskProcess=Start-Process -FilePath $taskExecutable -WindowStyle Hidden -PassThru
  $taskStartedTicks=$taskProcess.StartTime.ToUniversalTime().Ticks
  $taskIdentityMatched=Test-EnvironmentOwnProcess $taskProcess $taskExecutable $taskStartedTicks
  $taskExited=$taskProcess.WaitForExit(15000)
  if($taskExited){$taskProcess.Refresh();$taskExit=$taskProcess.ExitCode}
}finally{
  if($null -ne $taskProcess){
    if(-not $taskExited -and (Test-EnvironmentOwnProcess $taskProcess $taskExecutable $taskStartedTicks)){
      $taskForced=$true;Stop-Process -Id $taskProcess.Id -ErrorAction Stop
      $null=$taskProcess.WaitForExit(3000)
    }
    $taskProcess.Dispose()
  }
}
$taskReportPath=Join-Path $taskRun 'native-report.json'
$taskNative=$null
if(Test-Path -LiteralPath $taskReportPath -PathType Leaf){$taskNative=Get-Content -LiteralPath $taskReportPath -Raw|ConvertFrom-Json}
$taskValid=$null -ne $taskNative -and $taskNative.passed -and $taskNative.readOnlyObservationContract -and $taskNative.normalCompletion -and
  $taskNative.topologyKnownBefore -and $taskNative.topologyKnownAfter -and $taskNative.topologyStableAcrossObservations -and $taskNative.currentOutputCount -ge 1 -and
  $taskNative.currentOutputCount -le 16 -and $taskNative.topologyCaptureSerialBefore -eq 1 -and $taskNative.topologyCaptureSerialAfter -eq 2 -and $taskNative.declaredTopologyGeneration -eq 7 -and
  $taskNative.environmentCaptureCompleted -and $taskNative.samples.Count -eq 2 -and ($taskNative.knownEnvironmentSamples+$taskNative.unknownEnvironmentSamples) -eq 2 -and
  $taskNative.topologyTemporaryWindowCleanupConfirmed -and $taskNative.topologyTemporaryWindowsCreated -eq (4*$taskNative.currentOutputCount) -and
  $taskNative.topologyTemporaryWindowsCreated -eq $taskNative.topologyTemporaryWindowsReleased -and $taskNative.topologyOutstandingWindows -eq 0 -and
  -not $taskNative.nativeFullscreenAcceptance -and -not $taskNative.physicalMultiMonitorAcceptance -and -not $taskNative.mixedDpiAcceptance -and -not $taskNative.actualSessionTransitionTested -and
  -not $taskNative.fixtureCreated -and -not $taskNative.inputInjected -and -not $taskNative.focusChanged -and -not $taskNative.displaySettingsChanged -and -not $taskNative.desktopAttached -and
  -not $taskNative.browserStarted -and -not $taskNative.hostIntegrated
if($taskValid){
  $taskKnown=0;$taskUnknown=0
  foreach($taskSample in $taskNative.samples){
    if($taskSample.topologyGeneration -ne 7){$taskValid=$false}
    if($taskSample.known){
      $taskKnown++
      if($taskSample.outputs.Count -ne $taskNative.currentOutputCount -or $taskSample.globalBlocks -ne 'None'){$taskValid=$false}
    }else{
      $taskUnknown++
      if($taskSample.outputs.Count -ne 0 -or $taskSample.globalBlocks -notlike '*EnvironmentUnknown*' -or $taskSample.reason -eq 'None'){$taskValid=$false}
    }
  }
  if($taskKnown -ne $taskNative.knownEnvironmentSamples -or $taskUnknown -ne $taskNative.unknownEnvironmentSamples -or [bool]$taskNative.environmentAvailabilityConfirmed -ne ($taskKnown -eq 2)){$taskValid=$false}
}
$taskPassed=$taskIdentityMatched -and $taskExited -and -not $taskForced -and $taskExit -eq 0 -and $taskValid
$taskResult=[pscustomobject]@{passed=$taskPassed;processExitedWithinDeadline=$taskExited;
  normalProcessExit=($taskExited -and $taskExit -eq 0 -and $null -ne $taskNative -and $taskNative.normalCompletion);forcedTermination=$taskForced;exitCode=$taskExit;
  launchIdentityMatchedWhileRunning=$taskIdentityMatched;native=$taskNative;outputDirectory=$taskRun}
$taskResult|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskResult|ConvertTo-Json -Depth 8
if(-not $taskPassed){throw 'Per-output environment observation did not complete its read-only contract; inspect the private fixed-reason report.'}
