param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$DesktopLayer,[switch]$VisibleDesktop,[switch]$HostPolicy)
$ErrorActionPreference='Stop'
if($VisibleDesktop){$DesktopLayer=$true}
if($HostPolicy -and $DesktopLayer){throw 'Run controlled policy and desktop tests separately.'}
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory -DesktopLayer:$DesktopLayer
$taskRun=$taskBuild.outputDirectory
$taskProfile=Join-Path $taskRun 'private-profile'
$taskKnown=@{}
$taskProcess=$null;$taskRootIdentity=$null;$taskFailure=$null;$taskForcedCleanup=$false

function Read-DualIdentity($Metadata) {
  $taskCandidate=$null
  try{
    if($null -eq $Metadata -or $null -eq $Metadata.CreationDate -or -not $Metadata.ExecutablePath){return $null}
    $taskCandidate=Get-Process -Id ([int]$Metadata.ProcessId) -ErrorAction Stop
    $taskStart=$taskCandidate.StartTime.ToUniversalTime()
    if($taskCandidate.HasExited -or [Math]::Abs(($Metadata.CreationDate.ToUniversalTime()-$taskStart).TotalMilliseconds) -gt 2 -or
      -not $taskCandidate.Path.Equals($Metadata.ExecutablePath,[StringComparison]::OrdinalIgnoreCase)){return $null}
    return [pscustomobject]@{id=[int]$Metadata.ProcessId;parentId=[int]$Metadata.ParentProcessId;startTicks=$taskStart.Ticks;path=$Metadata.ExecutablePath;command=$Metadata.CommandLine}
  }catch{return $null}finally{if($null -ne $taskCandidate){$taskCandidate.Dispose()}}
}
function Test-DualIdentity($Identity) {
  $taskCandidate=$null
  try{
    $taskCandidate=Get-Process -Id $Identity.id -ErrorAction Stop
    return -not $taskCandidate.HasExited -and $taskCandidate.StartTime.ToUniversalTime().Ticks -eq $Identity.startTicks -and
      $taskCandidate.Path.Equals($Identity.path,[StringComparison]::OrdinalIgnoreCase)
  }catch{return $false}finally{if($null -ne $taskCandidate){$taskCandidate.Dispose()}}
}
function Update-DualOwned {
  $taskRows=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe' OR Name='DualWindowHost.exe'" -OperationTimeoutSec 5)
  $taskCurrent=@{}
  foreach($taskRow in $taskRows){$taskIdentity=Read-DualIdentity $taskRow;if($null -ne $taskIdentity){$taskCurrent[$taskIdentity.id]=$taskIdentity}}
  $taskLive=@{}
  foreach($taskKnownId in @($taskKnown.Keys)){
    if($taskCurrent.ContainsKey($taskKnownId) -and $taskCurrent[$taskKnownId].startTicks -eq $taskKnown[$taskKnownId].startTicks){$taskLive[$taskKnownId]=$taskCurrent[$taskKnownId]}
  }
  do{
    $taskAdded=$false
    foreach($taskIdentity in $taskCurrent.Values){
      if($taskLive.ContainsKey($taskIdentity.id) -or -not $taskLive.ContainsKey($taskIdentity.parentId)){continue}
      $taskParent=$taskLive[$taskIdentity.parentId]
      if($taskIdentity.startTicks -lt $taskParent.startTicks -or [IO.Path]::GetFileName($taskIdentity.path) -ine 'msedgewebview2.exe'){continue}
      if(-not (Test-DualIdentity $taskParent) -or -not (Test-DualIdentity $taskIdentity)){continue}
      # First browser child must name our unique profile; grandchildren need the
      # still-live, start-time-matched parent already proven to belong to us.
      if($taskParent.id -eq $taskRootIdentity.id -and (-not $taskIdentity.command -or -not $taskIdentity.command.Contains($taskProfile))){continue}
      $taskKnown[$taskIdentity.id]=$taskIdentity;$taskLive[$taskIdentity.id]=$taskIdentity;$taskAdded=$true
    }
  }while($taskAdded)
  return @($taskLive.Values)
}
function Stop-DualOwned($Identity) {
  $taskOwned=$null
  try{
    $taskOwned=Get-Process -Id $Identity.id -ErrorAction Stop
    if($taskOwned.HasExited){return}
    if($taskOwned.StartTime.ToUniversalTime().Ticks -ne $Identity.startTicks -or -not $taskOwned.Path.Equals($Identity.path,[StringComparison]::OrdinalIgnoreCase)){throw 'Owned process identity changed; refusing cleanup.'}
    $taskOwned.Kill();if(-not $taskOwned.WaitForExit(5000)){throw 'Owned process did not exit.'}
  }catch [Microsoft.PowerShell.Commands.ProcessCommandException] {return}
  finally{if($null -ne $taskOwned){$taskOwned.Dispose()}}
}
try{
  $taskMode=if($HostPolicy){'--policy-self-test'}elseif($VisibleDesktop){'--desktop-visible-self-test'}elseif($DesktopLayer){'--desktop-self-test'}else{'--self-test'}
  $taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList $taskMode -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskRootIdentity=[pscustomobject]@{id=$taskProcess.Id;startTicks=$taskProcess.StartTime.ToUniversalTime().Ticks;path=$taskBuild.executable}
  $taskKnown[$taskRootIdentity.id]=$taskRootIdentity
  $taskTimer=[Diagnostics.Stopwatch]::StartNew()
  while(-not $taskProcess.WaitForExit(100)){
    Update-DualOwned | Out-Null
    if($taskTimer.Elapsed.TotalSeconds -gt 75){throw 'Dual-window self-test timed out.'}
  }
  if($taskProcess.ExitCode -ne 0){throw ('Dual-window self-test exited '+$taskProcess.ExitCode+'.')}
  $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'webview-summary.json') -Raw | ConvertFrom-Json
  $taskExpectedChecks=if($HostPolicy){15}elseif($VisibleDesktop){16}elseif($DesktopLayer){15}else{11}
  if(-not $taskReport.passed -or $taskReport.value -ne 10 -or $taskReport.sequence -ne 2 -or $taskReport.checks.Count -ne $taskExpectedChecks){throw 'Dual-window self-test report mismatch.'}
  $taskDesktopReport=$null
  if($HostPolicy -and (-not $taskReport.hostPolicy.passed -or -not $taskReport.hostPolicy.pausedClockStable -or -not $taskReport.hostPolicy.noAutomaticLeaseRestore)){throw 'Host policy report mismatch.'}
  if($DesktopLayer){
    $taskDesktopReport=Get-Content -LiteralPath (Join-Path $taskRun 'desktop-summary.json') -Raw | ConvertFrom-Json
    if(-not $taskDesktopReport.passed -or $taskDesktopReport.attachments -ne 4 -or $taskDesktopReport.detachments -ne 4 -or
      $taskDesktopReport.remainingLeases -ne 0 -or -not $taskDesktopReport.foregroundUnchanged -or
      $taskDesktopReport.recoveredViewLosses -ne 1 -or $taskDesktopReport.lastLossReason -ne 'attached-window-changed'){throw 'Desktop shutdown report mismatch.'}
    if($VisibleDesktop -and (-not $taskDesktopReport.visual.passed -or -not $taskDesktopReport.visual.markersVerified -or $taskDesktopReport.visual.renderedFrames -lt 3)){throw 'Visible rendering report mismatch.'}
  }
}catch{$taskFailure=$_}
finally{
  try{
    if($null -ne $taskProcess -and -not $taskProcess.HasExited){
      Update-DualOwned | Out-Null
      Stop-DualOwned $taskRootIdentity;$taskForcedCleanup=$true
    }
    $taskCleanupTimer=[Diagnostics.Stopwatch]::StartNew();$taskRemaining=@()
    do{
      if($null -ne $taskRootIdentity){$taskRemaining=@(Update-DualOwned)}
      if($taskRemaining.Count -eq 0){break}
      Start-Sleep -Milliseconds 100
    }while($taskCleanupTimer.ElapsedMilliseconds -lt 15000)
    foreach($taskIdentity in ($taskRemaining | Sort-Object startTicks -Descending)){Stop-DualOwned $taskIdentity;$taskForcedCleanup=$true}
    if($null -ne $taskRootIdentity -and @(Update-DualOwned).Count -gt 0){throw 'Owned process cleanup remains unconfirmed.'}
    # Unknown processes with this private profile are reported, never adopted or killed.
    $taskUnknown=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -OperationTimeoutSec 5 | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskProfile)})
    if($taskUnknown.Count -gt 0){throw 'Dedicated-profile processes remain; refusing broad cleanup.'}
    if($taskForcedCleanup -and -not $taskFailure){throw 'Test required forced cleanup; normal lifecycle was not verified.'}
  }catch{
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $taskRun 'cleanup-error.txt') -Encoding utf8
    if(-not $taskFailure){$taskFailure=$_}
  }
  if($null -ne $taskProcess){$taskProcess.Dispose()}
}
if($taskFailure){throw $taskFailure}
[pscustomobject]@{passed=$true;checks=$taskReport.checks;value=$taskReport.value;sequence=$taskReport.sequence;
  realWebViewMessages=$true;desktopLayer=$taskDesktopReport;hostPolicy=$taskReport.hostPolicy;nativeInputTested=$false;forcedCleanup=$false;remainingDedicatedProcesses=0;outputDirectory=$taskRun} |
  ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
