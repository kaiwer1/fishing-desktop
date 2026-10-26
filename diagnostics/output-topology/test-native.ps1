param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
$taskExecutable=[IO.Path]::GetFullPath($taskBuild.executable)
$taskRun=$taskBuild.outputDirectory
$taskProcess=$null;$taskStartedTicks=0L;$taskForced=$false;$taskNormal=$false;$taskIdentityMatched=$false;$taskExit=$null
function Test-TopologyOwnProcess($Process,[string]$ExpectedPath,[long]$ExpectedStart) {
  if($null -eq $Process){return $false}
  try{
    $Process.Refresh()
    return (-not $Process.HasExited -and [IO.Path]::GetFullPath($Process.MainModule.FileName).Equals($ExpectedPath,[StringComparison]::OrdinalIgnoreCase) -and $Process.StartTime.ToUniversalTime().Ticks -eq $ExpectedStart)
  }catch{return $false}
}
try{
  $taskProcess=Start-Process -FilePath $taskExecutable -WindowStyle Hidden -PassThru
  $taskStartedTicks=$taskProcess.StartTime.ToUniversalTime().Ticks
  # Use the exact process returned by launch. Never search/adopt/terminate by name.
  $taskIdentityMatched=Test-TopologyOwnProcess $taskProcess $taskExecutable $taskStartedTicks
  $taskNormal=$taskProcess.WaitForExit(15000)
  if($taskNormal){$taskProcess.Refresh();$taskExit=$taskProcess.ExitCode}
}finally{
  if($null -ne $taskProcess){
    if(-not $taskNormal -and (Test-TopologyOwnProcess $taskProcess $taskExecutable $taskStartedTicks)){
      $taskForced=$true;Stop-Process -Id $taskProcess.Id -ErrorAction Stop
      $null=$taskProcess.WaitForExit(3000)
    }
    $taskProcess.Dispose()
  }
}
$taskReportPath=Join-Path $taskRun 'native-report.json'
$taskNative=$null
if(Test-Path -LiteralPath $taskReportPath -PathType Leaf){$taskNative=Get-Content -LiteralPath $taskReportPath -Raw|ConvertFrom-Json}
$taskPassed=$taskIdentityMatched -and $taskNormal -and -not $taskForced -and $taskExit -eq 0 -and $null -ne $taskNative -and $taskNative.passed -and $taskNative.normalCompletion -and $taskNative.cleanupConfirmed -and $taskNative.outstandingWindows -eq 0
$taskResult=[pscustomobject]@{passed=$taskPassed;processExitedWithinDeadline=$taskNormal;
  normalProcessExit=($taskNormal -and $taskExit -eq 0 -and $null -ne $taskNative -and $taskNative.normalCompletion);forcedTermination=$taskForced;exitCode=$taskExit;
  launchIdentityMatchedWhileRunning=$taskIdentityMatched;native=$taskNative;outputDirectory=$taskRun}
$taskResult|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskResult|ConvertTo-Json -Depth 8
if(-not $taskPassed){throw 'Read-only topology native probe failed or was unavailable; inspect the private fixed-reason report.'}
