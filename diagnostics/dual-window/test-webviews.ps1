param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$DesktopLayer,[switch]$VisibleDesktop,[switch]$HostPolicy,[switch]$NativePolicy,[switch]$RendererPolicy,[switch]$WallpaperPolicy,[switch]$DisplayPolicy,[ValidateRange(0,7200)][int]$SoakSeconds=0,[switch]$StartupPreparation,[switch]$OperatorFullscreen,[switch]$DisplaySoak,[switch]$ThreeScene,[switch]$Game,[ValidateSet('known-close','after-flush','after-prefix')][string]$GameFailureCase,[switch]$GameSpares,[switch]$GameCamp,[switch]$GameLineBreak,[switch]$GameWater,[switch]$GameScene,[switch]$GameAtlas,[switch]$GamePlayer,[switch]$GameWardrobe,[switch]$GameCatch,[switch]$GameWallpaper,[switch]$GameWallpaperPrepare)
$ErrorActionPreference='Stop'
if($GameLineBreak -and ($GameSpares -or $GameCamp)){throw 'Run line-break integration separately.'}
if($GameWater -and ($GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run water integration separately.'}
if($GameScene -and ($GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run scene integration separately.'}
if($GameAtlas -and ($GameScene -or $GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run atlas integration separately.'}
if($GamePlayer -and ($GameAtlas -or $GameScene -or $GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run player integration separately.'}
if($GameWardrobe -and ($GamePlayer -or $GameAtlas -or $GameScene -or $GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run wardrobe integration separately.'}
if($GameCatch -and ($GameWardrobe -or $GamePlayer -or $GameAtlas -or $GameScene -or $GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run catch integration separately.'}
if($GameWallpaperPrepare){$GameWallpaper=$true}
if($GameWallpaper -and ($GameCatch -or $GameWardrobe -or $GamePlayer -or $GameAtlas -or $GameScene -or $GameWater -or $GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak)){throw 'Run wallpaper game integration separately.'}
if($GameWater -or $GameScene -or $GameAtlas -or $GamePlayer -or $GameWardrobe -or $GameCatch -or $GameWallpaper){$Game=$true}
if(($GameSpares -and $GameFailureCase) -or ($GameCamp -and ($GameSpares -or $GameFailureCase))){throw 'Run game integration modes separately.'}
if($GameFailureCase -or $GameSpares -or $GameCamp -or $GameLineBreak){$Game=$true}
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
  if($GameFailureCase){$taskMode='--game-failure-self-test '+$GameFailureCase}
  if($GameSpares){$taskMode='--game-spares-self-test'}
  if($GameCamp){$taskMode='--game-camp-self-test'}
  if($GameWallpaper){$taskMode=if($GameWallpaperPrepare){'--game-wallpaper-prepare-self-test'}else{'--game-wallpaper-self-test'}}
  if($GameCatch){$taskMode='--game-catch-self-test'}
  if($GameWardrobe){$taskMode='--game-wardrobe-self-test'}
  if($GamePlayer){$taskMode='--game-player-self-test'}
  if($GameAtlas){$taskMode='--game-atlas-self-test'}
  if($GameScene){$taskMode='--game-scene-self-test'}
  if($GameWater){$taskMode='--game-water-self-test'}
  if($GameLineBreak){$taskMode=if($GameFailureCase){'--game-line-break-failure-self-test '+$GameFailureCase}else{'--game-line-break-self-test'}}
  $taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList $taskMode -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskRootIdentity=[pscustomobject]@{id=$taskProcess.Id;startTicks=$taskProcess.StartTime.ToUniversalTime().Ticks;path=$taskBuild.executable}
  $taskKnown[$taskRootIdentity.id]=$taskRootIdentity
  $taskTimer=[Diagnostics.Stopwatch]::StartNew()
  while(-not $taskProcess.WaitForExit(500)){
    Update-DualOwned | Out-Null
    $taskTimeout=if($Game){255}elseif($NativePolicy){105+$SoakSeconds+$(if($OperatorFullscreen){180}else{0})}else{75}
    if($taskTimer.Elapsed.TotalSeconds -gt $taskTimeout){throw 'Dual-window self-test timed out.'}
  }
  $taskObservedExit=$taskProcess.ExitCode
  $taskExpectedExit=if($GameFailureCase){4}else{0}
  if($taskObservedExit -ne $taskExpectedExit){throw ('Dual-window self-test exited '+$taskObservedExit+'.')}
  $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'webview-summary.json') -Raw | ConvertFrom-Json
  $taskExpectedChecks=if($Game){60}elseif($ThreeScene){if($SoakSeconds -gt 0){31}else{30}}elseif($DisplaySoak){29}elseif($OperatorFullscreen){34}elseif($StartupPreparation){16}elseif($DisplayPolicy){28}elseif($SoakSeconds -gt 0){34}elseif($WallpaperPolicy){33}elseif($RendererPolicy){21}elseif($NativePolicy){26}elseif($HostPolicy){17}elseif($VisibleDesktop){16}elseif($DesktopLayer){15}else{11}
  if($GameFailureCase){
    $taskReopened=if($GameFailureCase -eq 'known-close'){'interrupted-recovery'}elseif($GameFailureCase -eq 'after-flush'){if($GameLineBreak){'confirmed-line-break'}else{'confirmed-unhook'}}else{'corrupt-rejected'}
    if($GameLineBreak -and (-not $taskReport.lineBreakConnected -or $taskReport.controlledSlackSetup)){throw 'Physical loss failure scope mismatch.'}
    if($taskReport.documentReloadTested -ne ($GameFailureCase -eq 'known-close')){throw 'Failure reload evidence scope mismatch.'}
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 6 -or $taskReport.failureBoundary -ne $GameFailureCase -or $taskReport.expectedExitCode -ne 4 -or $taskReport.reopened -ne $taskReopened -or -not $taskReport.sourcePreserved -or -not $taskReport.privateCopyReopen -or $taskReport.secondHostProcessTested -or $taskReport.realDiskFailureTested){throw 'Failure boundary report mismatch.'}
    if(Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt')){throw 'Failure boundary test reported an unexpected host error.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if($taskGameExit.normalExit -or $taskGameExit.code -ne 'unconfirmed-failure-no-settlement'){throw 'Unconfirmed failure exit was not preserved.'}
  }elseif($GameWallpaper){
    if($taskReport.blocked){throw ('Desktop environment did not permit wallpaper verification: '+$taskReport.reason)}
    $taskDesktopReport=Get-Content -LiteralPath (Join-Path $taskRun 'desktop-summary.json') -Raw | ConvertFrom-Json
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 9 -or -not $taskReport.wallpaperAccepted -or -not $taskReport.ownedWindowBehindIcons -or -not $taskReport.observerReadOnly -or -not $taskReport.actualWebGL -or -not $taskReport.noActivateVerified -or ((-not $GameWallpaperPrepare) -and -not $taskReport.passiveExistingLayer) -or -not $taskReport.fullscreenPolicyApplied -or $taskReport.desktopIconInteractionTested -or $taskReport.desktopCompositionCaptured -or $taskReport.systemWallpaperPreferenceChanged){throw 'Wallpaper scene evidence mismatch.'}
    if($GameWallpaperPrepare -and (-not $taskReport.prepareEnabled -or $taskReport.messageAttempts -gt 1 -or $taskReport.requestCalls -gt 1)){throw 'Unexpected desktop layer preparation evidence.'}
    if(-not $taskDesktopReport.passed -or $taskDesktopReport.attachments -ne 1 -or $taskDesktopReport.detachments -ne 1 -or $taskDesktopReport.remainingLeases -ne 0 -or $taskReport.sequence -ne 1 -or $taskReport.value -ne 12000 -or -not (Test-Path -LiteralPath (Join-Path $taskRun 'game-wallpaper.png') -PathType Leaf)){throw 'Desktop layer or clean wallpaper exit mismatch.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Wallpaper shutdown was not confirmed.'}
  }elseif($GameCatch){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 8 -or -not $taskReport.catchSaleConnected -or -not $taskReport.actual3dCast -or -not $taskReport.realMouseStrike -or -not $taskReport.manualReel -or -not $taskReport.ownedNetEquipped -or -not $taskReport.catchCardVerified -or -not $taskReport.creelPreviewVerified -or -not $taskReport.saleVerified -or $taskReport.rawGameCommandInjected -or $taskReport.controlledFightTime){throw 'Catch and sale input scope mismatch.'}
    if($taskReport.sequence -ne 6 -or $taskReport.saleValueMinor -le 0 -or $taskReport.value -ne (9600+$taskReport.saleValueMinor) -or $taskReport.gameNaturalLandings -ne 1 -or $taskReport.creelCount -ne 0 -or $taskReport.gameFlightEmissions -ne 1 -or $taskReport.gameHookedEmissions -ne 1 -or $taskReport.baitCount -ne 11 -or $taskReport.stamina -ne 98 -or $taskReport.remainingLineMm -ne 50000 -or -not (Test-Path -LiteralPath (Join-Path $taskRun 'game-catch.png') -PathType Leaf)){throw 'Catch sale accounting mismatch.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Catch shutdown was not confirmed.'}
  }elseif($GameWardrobe){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 9 -or -not $taskReport.wardrobeConnected -or -not $taskReport.visibleSleeveChanged -or $taskReport.blueSleevePixels -lt 35 -or $taskReport.confirmedPurchases -ne 2 -or $taskReport.ownedClothing -ne 2 -or $taskReport.wornClothing -ne 'coast-jacket' -or -not $taskReport.reloadVerified -or -not $taskReport.closedJournalVerified){throw 'Wardrobe/visible sleeve validation mismatch.'}
    if($taskReport.sequence -ne 8 -or $taskReport.value -ne 7600 -or $taskReport.baitCount -ne 12 -or $taskReport.stamina -ne 100 -or $taskReport.remainingLineMm -ne 50000){throw 'Wardrobe resource mismatch.'}
    foreach($image in 'wardrobe-default.png','wardrobe-coast.png'){if(-not (Test-Path -LiteralPath (Join-Path $taskRun $image) -PathType Leaf)){throw 'Wardrobe preview missing.'}}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Wardrobe shutdown was not confirmed.'}
  }elseif($GamePlayer){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 8 -or -not $taskReport.playerWindowConnected -or -not $taskReport.singleInteractionWindow -or -not $taskReport.distinctScenery -or -not $taskReport.coastalCast -or -not $taskReport.reloadRetainedStation -or $taskReport.observerOpened -or $taskReport.personalSaveRead -or $taskReport.desktopLayer -or $taskReport.controlledCharge){throw 'Player window scope mismatch.'}
    if($taskReport.sequence -ne 3 -or $taskReport.value -ne 12000 -or $taskReport.baitCount -ne 11 -or $taskReport.stamina -ne 98 -or $taskReport.mapCount -ne 3 -or $taskReport.speciesCount -ne 7 -or $taskReport.playerPreviewCount -ne 3 -or $taskReport.gameFlightEmissions -ne 1){throw 'Player window resources mismatch.'}
    foreach($image in 'player-lake.png','player-river.png','player-coast.png'){if(-not (Test-Path -LiteralPath (Join-Path $taskRun $image) -PathType Leaf)){throw 'Player lake capture missing.'}}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Player window shutdown was not confirmed.'}
  }elseif($GameAtlas){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 10 -or -not $taskReport.atlasConnected -or $taskReport.mapCount -ne 3 -or $taskReport.speciesCount -ne 7 -or $taskReport.actualPoolDraws -ne 2 -or $taskReport.riverPool -ne 'shallow' -or $taskReport.coastalPool -ne 'deep' -or -not $taskReport.returnedToStartingWater -or $taskReport.spotPersisted -or $taskReport.controlledCharge){throw 'Atlas travel and habitat evidence mismatch.'}
    if($taskReport.sequence -ne 5 -or $taskReport.value -ne 12000 -or $taskReport.baitCount -ne 10 -or $taskReport.stamina -ne 96 -or $taskReport.gameFlightEmissions -ne 2 -or $taskReport.remainingLineMm -ne 50000 -or -not (Test-Path -LiteralPath (Join-Path $taskRun 'game-atlas-panel.png') -PathType Leaf)){throw 'Atlas accounting or map screen missing.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Atlas shutdown was not confirmed.'}
  }elseif($GameScene){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 10 -or -not $taskReport.gameSceneConnected -or -not $taskReport.actualWebGL -or -not $taskReport.observerReadOnly -or -not $taskReport.fallbackAimVerified -or -not $taskReport.controlledContextLoss -or -not $taskReport.realHostFlight -or -not $taskReport.devToolsMouseInput -or $taskReport.controlledCharge -or $taskReport.nativePhysicalMouse){throw 'First-person scene evidence mismatch.'}
    if($taskReport.triangles -le 0 -or $taskReport.sampleColors -lt 4 -or -not $taskReport.glVersion -or -not $taskReport.rendererName -or -not (Test-Path -LiteralPath (Join-Path $taskRun 'game-scene-panel.png') -PathType Leaf)){throw 'Missing actual lake render.'}
    if($taskReport.sequence -ne 3 -or $taskReport.value -ne 12000 -or $taskReport.gameFlightEmissions -ne 1 -or $taskReport.baitCount -ne 11 -or $taskReport.stamina -ne 98 -or $taskReport.equipmentInstances -ne 6 -or $taskReport.remainingLineMm -ne 50000 -or $taskReport.lostLineMm -ne 0){throw 'First-person cast conservation mismatch.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session' -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Scene shutdown was not confirmed.'}
  }elseif($GameWater){
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 12 -or -not $taskReport.waterAimConnected -or -not $taskReport.sharedWaterGeometry -or -not $taskReport.realHostFlight -or -not $taskReport.naturalHookedTarget -or -not $taskReport.devToolsMouseInput -or $taskReport.nativePhysicalMouse -or $taskReport.pointerCaptureStubbed -or -not $taskReport.controlledCharge){throw 'Water targeting evidence mismatch.'}
    if($taskReport.schemaVersion -ne 3 -or $taskReport.sequence -ne 11 -or $taskReport.value -ne 12000 -or $taskReport.baitCount -ne 7 -or $taskReport.stamina -ne 90 -or $taskReport.gameFlightEmissions -ne 5 -or $taskReport.equipmentInstances -ne 6 -or $taskReport.remainingLineMm -ne 50000 -or $taskReport.lostLineMm -ne 0){throw 'Water targeting resource mismatch.'}
    if($taskReport.leftXmm -ge 0 -or $taskReport.rightXmm -le 0 -or $taskReport.farZmm -le $taskReport.nearZmm -or $taskReport.lowPowerZmm -ge $taskReport.farZmm -or $taskReport.lowPowerPermille -ge 1000 -or $taskReport.hookYmm -ne -200){throw 'Water targeting trajectory mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-water-panel.png') -PathType Leaf) -or (Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt'))){throw 'Water targeting preview or clean exit missing.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session'){throw 'Water targeting shutdown was not confirmed.'}
  }elseif($GameLineBreak){
    if(-not $taskReport.passed -or -not $taskReport.lineBreakConnected -or -not $taskReport.physicalOverload -or $taskReport.controlledTerminalLoss -or $taskReport.checks.Count -ne 10 -or $taskReport.sequence -ne 14 -or $taskReport.value -ne 11420){throw 'Physical line-break report mismatch.'}
    if($taskReport.gameLineBreakConfirmations -ne 1 -or $taskReport.baitCount -ne 10 -or $taskReport.stamina -ne 96 -or $taskReport.gameFlightEmissions -ne 2 -or $taskReport.equipmentInstances -ne 10){throw 'Physical loss resource mismatch.'}
    if($taskReport.initialLineMm -ne 100000 -or $taskReport.lostLineMm -le 0 -or ($taskReport.remainingLineMm+$taskReport.lostLineMm) -ne 100000 -or $taskReport.breakTensionN -le 5 -or $taskReport.breakThresholdMilliN -ne 5000){throw 'Physical loss conservation or evidence mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-line-break-panel.png') -PathType Leaf)){throw 'Physical loss page preview missing.'}
    if(Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt')){throw 'Physical line-break integration reported an error.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session'){throw 'Physical line-break shutdown was not confirmed.'}
  }elseif($GameCamp){
    if(-not $taskReport.passed -or -not $taskReport.gameLoaded -or -not $taskReport.campWorkConnected -or $taskReport.checks.Count -ne 12 -or $taskReport.schemaVersion -ne 3 -or $taskReport.sequence -ne 12 -or $taskReport.value -ne 760){throw 'Camp recovery transaction report mismatch.'}
    if($taskReport.campRewardConfirmations -ne 4 -or -not $taskReport.controlledCampTime -or $taskReport.realFullMinute -or -not $taskReport.shortHostClockSample){throw 'Camp active-time evidence mismatch.'}
    if($taskReport.baitCount -ne 4 -or $taskReport.stamina -ne 18 -or $taskReport.foodCount -ne 0 -or -not $taskReport.ownedNetEquipped -or $taskReport.equipmentInstances -ne 6 -or $taskReport.remainingLineMm -ne 50000 -or $taskReport.gameFlightEmissions -ne 1){throw 'Camp recovery resource report mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'camp-panel.png') -PathType Leaf)){throw 'Camp menu preview was not captured.'}
    if(Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt')){throw 'Camp integration reported an unexpected host error.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session'){throw 'Camp shutdown was not confirmed.'}
  }elseif($GameSpares){
    if(-not $taskReport.passed -or -not $taskReport.gameLoaded -or $taskReport.schemaVersion -ne 3 -or $taskReport.value -ne 11385 -or $taskReport.sequence -ne 16 -or $taskReport.checks.Count -ne 15){throw 'Spares integration transaction report mismatch.'}
    if(-not $taskReport.sparesMenuConnected -or -not $taskReport.controlledTerminalLoss -or $taskReport.equipmentDamageImplemented -or $taskReport.equipmentInstances -ne 8 -or $taskReport.initialLineMm -ne 100000 -or $taskReport.remainingLineMm -ne 80000 -or $taskReport.lostLineMm -ne 20000 -or -not $taskReport.equipmentArchiveConnected -or $taskReport.archivedEquipmentInstances -ne 3){throw 'Spares integration ownership report mismatch.'}
    if($taskReport.baitCount -ne 10 -or $taskReport.stamina -ne 96 -or $taskReport.foodCount -ne 0 -or $taskReport.ownedNetEquipped -or $taskReport.gameFlightEmissions -ne 2 -or $taskReport.terminalLossConfirmations -ne 1){throw 'Spares integration resource report mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-spares-panel.png') -PathType Leaf)){throw 'Spares menu preview was not captured.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-archive-panel.png') -PathType Leaf)){throw 'Equipment archive preview was not captured.'}
    if(Test-Path -LiteralPath (Join-Path $taskRun 'dual-window-error.txt')){throw 'Spares integration reported an unexpected host error.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'no-active-session'){throw 'Spares shutdown was not confirmed.'}
  }elseif($Game){
    if(-not $taskReport.equipmentMenuConnected -or $taskReport.schemaVersion -ne 3 -or $taskReport.equipmentInstances -ne 6 -or $taskReport.remainingLineMm -ne 50000){throw 'Equipment menu and conserved assets report mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-equipment-panel.png') -PathType Leaf)){throw 'Equipment menu preview was not captured.'}
    if(-not $taskReport.passed -or -not $taskReport.gameLoaded -or $taskReport.value -ne 9640 -or $taskReport.sequence -ne 35 -or $taskReport.checks.Count -ne $taskExpectedChecks -or $taskReport.gameSimulationImplemented -or -not $taskReport.controlledFishFacts -or -not $taskReport.castPreparationConnected -or $taskReport.gameFlightEmissions -ne 10 -or -not $taskReport.flatWaterFlightConnected -or $taskReport.realWaterAndFlight -or -not $taskReport.biteSimulationConnected -or $taskReport.gameHookedEmissions -ne 5 -or -not $taskReport.naturalFishLanded -or -not $taskReport.controlledBiteSeed -or -not $taskReport.fightSimulationConnected -or $taskReport.gameNaturalLandings -ne 2 -or $taskReport.fixtureNetCapability -or -not $taskReport.commerceConnected -or -not $taskReport.ownedNetEquipped -or $taskReport.foodCount -ne 1 -or $taskReport.baitCount -ne 12 -or $taskReport.stamina -ne 90 -or -not $taskReport.slackUnhookConnected -or -not $taskReport.controlledSlackSetup -or $taskReport.gameUnhookedConfirmations -ne 1 -or $taskReport.equipmentDamageImplemented){throw 'Game commerce and fight integration report mismatch.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-cast-panel.png') -PathType Leaf)){throw 'Owned game page preview was not captured.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-flight-panel.png') -PathType Leaf)){throw 'Owned flight page preview was not captured.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-bite-panel.png') -PathType Leaf)){throw 'Owned bite page preview was not captured.'}
    if(-not (Test-Path -LiteralPath (Join-Path $taskRun 'game-fight-panel.png') -PathType Leaf)){throw 'Owned fight page preview was not captured.'}
    $taskGameExit=Get-Content -LiteralPath (Join-Path $taskRun 'game-shutdown.json') -Raw | ConvertFrom-Json
    if(-not $taskGameExit.normalExit -or $taskGameExit.code -ne 'recovered-candidate'){throw 'Game shutdown was not confirmed.'}
  }elseif(-not $taskReport.passed -or $taskReport.value -ne 10 -or $taskReport.sequence -ne 2 -or $taskReport.checks.Count -ne $taskExpectedChecks){throw 'Dual-window self-test report mismatch.'}
  if($ThreeScene -and (-not $taskReport.scene.passed -or -not $taskReport.scene.finalRecreatedViewVerified -or $taskReport.scene.sceneLoads -lt 5 -or
    -not $taskReport.scene.runtimeWatchdogEnabled -or $taskReport.scene.watchdogSamples -lt 3 -or
    $taskReport.scene.gameLoaded -or $taskReport.scene.nativeFullscreenTested -or $taskReport.scene.presentedFpsMeasured)){throw 'Three-dimensional scene report mismatch.'}
  if(-not $GameWallpaper){$taskDesktopReport=$null}
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
[pscustomobject]@{passed=$true;checks=$taskReport.checks;value=$taskReport.value;sequence=$taskReport.sequence;failureBoundary=$taskReport.failureBoundary;expectedExitCode=$taskExpectedExit;reopened=$taskReport.reopened;sourcePreserved=$taskReport.sourcePreserved;privateCopyReopen=$taskReport.privateCopyReopen;documentReloadTested=$taskReport.documentReloadTested;secondHostProcessTested=$false;realDiskFailureTested=$false;
  lineBreakConnected=$taskReport.lineBreakConnected;physicalOverload=$taskReport.physicalOverload;gameLineBreakConfirmations=$taskReport.gameLineBreakConfirmations;
  atlasConnected=$taskReport.atlasConnected;mapCount=$taskReport.mapCount;speciesCount=$taskReport.speciesCount;
  gameSceneConnected=$taskReport.gameSceneConnected;actualWebGL=$taskReport.actualWebGL;rendererName=$taskReport.rendererName;softwareRendererObserved=$taskReport.softwareRendererObserved;
  waterAimConnected=$taskReport.waterAimConnected;sharedWaterGeometry=$taskReport.sharedWaterGeometry;devToolsMouseInput=$taskReport.devToolsMouseInput;realHostFlight=$taskReport.realHostFlight;
  campWorkConnected=$taskReport.campWorkConnected;campRewardConfirmations=$taskReport.campRewardConfirmations;controlledCampTime=$taskReport.controlledCampTime;realFullMinute=$taskReport.realFullMinute;sparesMenuConnected=$taskReport.sparesMenuConnected;controlledTerminalLoss=$taskReport.controlledTerminalLoss;terminalLossConfirmations=$taskReport.terminalLossConfirmations;initialLineMm=$taskReport.initialLineMm;lostLineMm=$taskReport.lostLineMm;
  realWebViewMessages=$true;gameLoaded=[bool]$Game;gameSimulationImplemented=$false;controlledFishFacts=$taskReport.controlledFishFacts;castPreparationConnected=$taskReport.castPreparationConnected;gameFlightEmissions=$taskReport.gameFlightEmissions;flatWaterFlightConnected=$taskReport.flatWaterFlightConnected;realWaterAndFlight=$false;biteSimulationConnected=$taskReport.biteSimulationConnected;gameHookedEmissions=$taskReport.gameHookedEmissions;naturalFishLanded=$taskReport.naturalFishLanded;fightSimulationConnected=$taskReport.fightSimulationConnected;gameNaturalLandings=$taskReport.gameNaturalLandings;fixtureNetCapability=$taskReport.fixtureNetCapability;commerceConnected=$taskReport.commerceConnected;equipmentMenuConnected=$taskReport.equipmentMenuConnected;schemaVersion=$taskReport.schemaVersion;equipmentInstances=$taskReport.equipmentInstances;remainingLineMm=$taskReport.remainingLineMm;ownedNetEquipped=$taskReport.ownedNetEquipped;foodCount=$taskReport.foodCount;baitCount=$taskReport.baitCount;stamina=$taskReport.stamina;slackUnhookConnected=$taskReport.slackUnhookConnected;controlledSlackSetup=$taskReport.controlledSlackSetup;gameUnhookedConfirmations=$taskReport.gameUnhookedConfirmations;equipmentDamageImplemented=$false;desktopLayer=$taskDesktopReport;hostPolicy=$taskReport.hostPolicy;availability=$taskReport.availability;nativePolicy=$taskReport.nativePolicy;nativeForegroundTestSkipped=$taskReport.nativeForegroundTestSkipped;nativeSignals=$taskReport.nativeSignals;display=$taskReport.display;renderer=$taskReport.renderer;soak=$taskReport.soak;scene=$taskReport.scene;nativeInputTested=$false;forcedCleanup=$false;remainingDedicatedProcesses=0;outputDirectory=$taskRun} |
  ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
