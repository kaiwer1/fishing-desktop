param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$DesktopLayer,[switch]$VisibleDesktop,[switch]$HostPolicy,[switch]$NativePolicy,[switch]$RendererPolicy,[switch]$WallpaperPolicy,[switch]$DisplayPolicy,[ValidateRange(0,7200)][int]$SoakSeconds=0,[switch]$StartupPreparation,[switch]$OperatorFullscreen,[switch]$DisplaySoak,[switch]$ThreeScene,[switch]$Game)
$ErrorActionPreference='Stop'
if($Game -and ($ThreeScene -or $DesktopLayer -or $VisibleDesktop -or $HostPolicy -or $NativePolicy -or $RendererPolicy -or $WallpaperPolicy -or $DisplayPolicy -or $StartupPreparation -or $OperatorFullscreen -or $DisplaySoak -or $SoakSeconds -gt 0)){throw 'Run game data integration separately from diagnostic modes.'}
if($ThreeScene){
  if($DisplaySoak -or $OperatorFullscreen -or $StartupPreparation -or $DisplayPolicy -or $NativePolicy -or $WallpaperPolicy -or $VisibleDesktop -or $HostPolicy -or $RendererPolicy){throw 'Choose the independent three-dimensional scene mode without other switches.'}
  $DisplayPolicy=$true
}
if($DisplaySoak){
  if($OperatorFullscreen -or $StartupPreparation -or $DisplayPolicy -or $NativePolicy -or $WallpaperPolicy -or $VisibleDesktop -or $HostPolicy -or $RendererPolicy -or $SoakSeconds -lt 120){throw 'Choose independent display soak with at least 120 seconds and no other mode switches.'}
  $DisplayPolicy=$true
}
if($OperatorFullscreen){
  if($StartupPreparation -or $DisplayPolicy -or $SoakSeconds -gt 0){throw 'Run operator fullscreen separately from startup/display/soak modes.'}
  $WallpaperPolicy=$true
}
if($StartupPreparation){
  if($VisibleDesktop -or $HostPolicy -or $NativePolicy -or $RendererPolicy -or $WallpaperPolicy -or $DisplayPolicy -or $SoakSeconds -gt 0){throw 'Run startup preparation separately from other policy/visible modes.'}
  $DesktopLayer=$true
}
if($DisplayPolicy){if((-not ($DisplaySoak -or $ThreeScene) -and $SoakSeconds -gt 0) -or $NativePolicy -or $WallpaperPolicy){throw 'Choose independent display checks or the full native/soak mode.'};$WallpaperPolicy=$true}
if($SoakSeconds -gt 0){if($SoakSeconds -lt 120){throw 'SoakSeconds must be zero or at least 120.'};$WallpaperPolicy=$true}
if($WallpaperPolicy){$VisibleDesktop=$true;$NativePolicy=$true}
if($VisibleDesktop){$DesktopLayer=$true}
if($NativePolicy){$HostPolicy=$true}
if($RendererPolicy){$HostPolicy=$true}
if($RendererPolicy -and $NativePolicy){throw 'Run native foreground and renderer lifecycle tests separately.'}
if($HostPolicy -and $DesktopLayer -and -not $WallpaperPolicy){throw 'Use WallpaperPolicy for the combined test.'}
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory -DesktopLayer:$DesktopLayer -NativePolicy:$NativePolicy -ThreeScene:$ThreeScene -Game:$Game
$taskRun=$taskBuild.outputDirectory
$taskProfile=Join-Path $taskRun 'private-profile'
$taskKnown=@{}
$taskProcess=$null;$taskRootIdentity=$null;$taskFailure=$null;$taskForcedCleanup=$false
$taskCleanupConfirmed=$false;$taskObservedExit=$null

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
  $taskRows=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe' OR Name='DualWindowHost.exe' OR Name='NativeEnvironmentFixture.exe'" -OperationTimeoutSec 5)
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
      $taskIsFixture=$NativePolicy -and $taskParent.id -eq $taskRootIdentity.id -and $taskIdentity.path.Equals((Join-Path $taskRun 'NativeEnvironmentFixture.exe'),[StringComparison]::OrdinalIgnoreCase) -and $taskIdentity.command -and $taskIdentity.command.Contains($taskRun)
      if($taskIdentity.startTicks -lt $taskParent.startTicks -or (-not $taskIsFixture -and [IO.Path]::GetFileName($taskIdentity.path) -ine 'msedgewebview2.exe')){continue}
      if(-not (Test-DualIdentity $taskParent) -or -not (Test-DualIdentity $taskIdentity)){continue}
      # First browser child must name our unique profile; grandchildren need the
      # still-live, start-time-matched parent already proven to belong to us.
      if(-not $taskIsFixture -and $taskParent.id -eq $taskRootIdentity.id -and (-not $taskIdentity.command -or -not $taskIdentity.command.Contains($taskProfile))){continue}
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
  $taskMode=if($Game){'--game-self-test'}elseif($ThreeScene){if($SoakSeconds -gt 0){'--three-scene-soak-self-test '+$SoakSeconds}else{'--three-scene-self-test'}}elseif($DisplaySoak){'--display-soak-self-test '+$SoakSeconds}elseif($OperatorFullscreen){'--wallpaper-operator-self-test'}elseif($StartupPreparation){'--desktop-startup-self-test'}elseif($DisplayPolicy){'--display-policy-self-test'}elseif($SoakSeconds -gt 0){'--wallpaper-soak-self-test '+$SoakSeconds}elseif($WallpaperPolicy){'--wallpaper-policy-self-test'}elseif($RendererPolicy){'--renderer-self-test'}elseif($NativePolicy){'--native-policy-self-test'}elseif($HostPolicy){'--policy-self-test'}elseif($VisibleDesktop){'--desktop-visible-self-test'}elseif($DesktopLayer){'--desktop-self-test'}else{'--self-test'}
  $taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList $taskMode -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskRootIdentity=[pscustomobject]@{id=$taskProcess.Id;startTicks=$taskProcess.StartTime.ToUniversalTime().Ticks;path=$taskBuild.executable}
  $taskKnown[$taskRootIdentity.id]=$taskRootIdentity
  $taskTimer=[Diagnostics.Stopwatch]::StartNew()
  while(-not $taskProcess.WaitForExit(500)){
    Update-DualOwned | Out-Null
    $taskTimeout=if($NativePolicy){105+$SoakSeconds+$(if($OperatorFullscreen){180}else{0})}else{75}
    if($taskTimer.Elapsed.TotalSeconds -gt $taskTimeout){throw 'Dual-window self-test timed out.'}
  }
  $taskObservedExit=$taskProcess.ExitCode
  if($taskObservedExit -ne 0){throw ('Dual-window self-test exited '+$taskObservedExit+'.')}
  $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'webview-summary.json') -Raw | ConvertFrom-Json
  $taskExpectedChecks=if($Game){12}elseif($ThreeScene){if($SoakSeconds -gt 0){31}else{30}}elseif($DisplaySoak){29}elseif($OperatorFullscreen){34}elseif($StartupPreparation){16}elseif($DisplayPolicy){28}elseif($SoakSeconds -gt 0){34}elseif($WallpaperPolicy){33}elseif($RendererPolicy){21}elseif($NativePolicy){26}elseif($HostPolicy){17}elseif($VisibleDesktop){16}elseif($DesktopLayer){15}else{11}
  if($Game){
    if(-not $taskReport.passed -or -not $taskReport.gameLoaded -or $taskReport.value -ne 12120 -or $taskReport.sequence -ne 14 -or $taskReport.checks.Count -ne 12 -or $taskReport.gameSimulationImplemented -or -not $taskReport.controlledFishFacts){throw 'Game data integration report mismatch.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'recovered-candidate'){throw 'Game shutdown was not confirmed.'}
  }elseif(-not $taskReport.passed -or $taskReport.value -ne 10 -or $taskReport.sequence -ne 2 -or $taskReport.checks.Count -ne $taskExpectedChecks){throw 'Dual-window self-test report mismatch.'}
  if($ThreeScene -and (-not $taskReport.scene.passed -or -not $taskReport.scene.finalRecreatedViewVerified -or $taskReport.scene.sceneLoads -lt 5 -or
    -not $taskReport.scene.runtimeWatchdogEnabled -or $taskReport.scene.watchdogSamples -lt 3 -or
    $taskReport.scene.gameLoaded -or $taskReport.scene.nativeFullscreenTested -or $taskReport.scene.presentedFpsMeasured)){throw 'Three-dimensional scene report mismatch.'}
  $taskDesktopReport=$null
  $taskRebuilds=0
  if($SoakSeconds -gt 0){
    if(-not $taskReport.soak.passed -or $taskReport.soak.requestedSeconds -ne $SoakSeconds -or $taskReport.soak.elapsedSeconds -lt $SoakSeconds -or $taskReport.soak.drawProgressChecks -lt 3){throw 'Soak report mismatch.'}
    $taskRebuilds=[int]$taskReport.soak.viewRebuilds
  }
  if($HostPolicy -and (-not $taskReport.hostPolicy.passed -or -not $taskReport.hostPolicy.pausedClockStable -or -not $taskReport.hostPolicy.noAutomaticLeaseRestore)){throw 'Host policy report mismatch.'}
  if($HostPolicy -and (-not $taskReport.availability.passed -or -not $taskReport.availability.zeroWindowsObserved -or -not $taskReport.availability.bothDocumentsReplaced -or -not $taskReport.availability.unavailableClockFrozen)){throw 'View availability report mismatch.'}
  if($OperatorFullscreen -and -not $taskReport.nativePolicy.operatorStartUsed){throw 'Operator fullscreen evidence is missing.'}
  if($NativePolicy -and -not $DisplayPolicy -and (-not $taskReport.nativePolicy.passed -or -not $taskReport.nativePolicy.fixtureNormalExit -or $taskReport.nativePolicy.maximumConsecutiveFullSamples -lt 10)){throw 'Native joint policy report mismatch.'}
  if($DisplayPolicy -and (-not $taskReport.nativeForegroundTestSkipped -or $null -ne $taskReport.nativePolicy)){throw 'Display mode must explicitly omit native foreground evidence.'}
  if($NativePolicy -and (-not $taskReport.nativeSignals.passed -or -not $taskReport.nativeSignals.notificationWindowRegistered -or -not $taskReport.nativeSignals.controlledCallbacks -or $taskReport.nativeSignals.actualLockOrPowerTransition)){throw 'Controlled native signal report mismatch.'}
  if($NativePolicy -and (-not $taskReport.display.passed -or -not $taskReport.display.controlledCallbacks -or $taskReport.display.actualDisplayOrDpiChange -or -not $taskReport.display.receiverPreserved)){throw 'Controlled display report mismatch.'}
  if($RendererPolicy -and (-not $taskReport.renderer.passed -or -not $taskReport.renderer.realWebViewSuspended -or -not $taskReport.renderer.suspendedDisposeRebuilt)){throw 'Real renderer policy report mismatch.'}
  if($DesktopLayer){
    $taskDesktopReport=Get-Content -LiteralPath (Join-Path $taskRun 'desktop-summary.json') -Raw | ConvertFrom-Json
    $taskExpectedAttachments=if($WallpaperPolicy){5+$taskRebuilds}else{4+$taskRebuilds}
    if(-not $taskDesktopReport.passed -or $taskDesktopReport.attachments -ne $taskExpectedAttachments -or $taskDesktopReport.detachments -ne $taskExpectedAttachments -or
      $taskDesktopReport.remainingLeases -ne 0 -or (-not $WallpaperPolicy -and -not $taskDesktopReport.foregroundUnchanged) -or
      $taskDesktopReport.recoveredViewLosses -ne 1 -or $taskDesktopReport.lastLossReason -ne 'attached-window-changed'){throw 'Desktop shutdown report mismatch.'}
    if($StartupPreparation -and (-not $taskDesktopReport.startup.enabled -or $taskDesktopReport.startup.prepareCalls -ne 4 -or
      $taskDesktopReport.startup.requestCalls -gt 1 -or $taskDesktopReport.startup.nativeMessageAttempts -gt 1 -or
      ($taskDesktopReport.startup.reusedExistingCalls+$taskDesktopReport.startup.requestCalls) -ne 4 -or $taskDesktopReport.startup.freshLoginTested)){throw 'Startup preparation report mismatch.'}
    if($VisibleDesktop -and (-not $taskDesktopReport.visual.passed -or -not $taskDesktopReport.visual.markersVerified -or $taskDesktopReport.visual.renderedFrames -lt 3)){throw 'Visible rendering report mismatch.'}
    $taskRendererCombined=if($DisplayPolicy){$taskReport.renderer.controlledSignalsCombined -and -not $taskReport.renderer.nativeFullscreenCombined}else{$taskReport.renderer.nativeFullscreenCombined}
    if($WallpaperPolicy -and (-not $taskDesktopReport.presentationChecksPassed -or $taskDesktopReport.presentationChecks -lt 3 -or -not $taskRendererCombined -or -not $taskReport.renderer.ownFrozenFrameVerified)){throw 'Combined wallpaper report mismatch.'}
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
    $taskCleanupConfirmed=$true
    if($taskForcedCleanup -and -not $taskFailure){throw 'Test required forced cleanup; normal lifecycle was not verified.'}
  }catch{
    $_.Exception.Message | Set-Content -LiteralPath (Join-Path $taskRun 'cleanup-error.txt') -Encoding utf8
    if(-not $taskFailure){$taskFailure=$_}
  }
  if($null -ne $taskProcess){$taskProcess.Dispose()}
}
if($taskFailure){
  [pscustomobject]@{passed=$false;mode=$taskMode;exitCode=$taskObservedExit;errorType=$taskFailure.Exception.GetType().FullName;
    message=$taskFailure.Exception.Message;nativeForegroundTestSkipped=[bool]$DisplayPolicy;forcedCleanup=$taskForcedCleanup;
    cleanupConfirmed=$taskCleanupConfirmed;remainingDedicatedProcesses=$(if($taskCleanupConfirmed){0}else{$null})} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'failure-summary.json') -Encoding utf8
  throw $taskFailure
}
[pscustomobject]@{passed=$true;checks=$taskReport.checks;value=$taskReport.value;sequence=$taskReport.sequence;
  realWebViewMessages=$true;gameLoaded=[bool]$Game;gameSimulationImplemented=$false;controlledFishFacts=[bool]$Game;desktopLayer=$taskDesktopReport;hostPolicy=$taskReport.hostPolicy;availability=$taskReport.availability;nativePolicy=$taskReport.nativePolicy;nativeForegroundTestSkipped=$taskReport.nativeForegroundTestSkipped;nativeSignals=$taskReport.nativeSignals;display=$taskReport.display;renderer=$taskReport.renderer;soak=$taskReport.soak;scene=$taskReport.scene;nativeInputTested=$false;forcedCleanup=$false;remainingDedicatedProcesses=0;outputDirectory=$taskRun} |
  ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
