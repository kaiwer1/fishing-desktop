param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[ValidateRange(60,600)][int]$Seconds=300,[switch]$UnattendedTimeoutCheck)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskRoot -DesktopLayer
$taskRun=$taskBuild.outputDirectory
$taskExecutable=$taskBuild.executable
$taskMode=if($UnattendedTimeoutCheck){'--manual-desktop-timeout-probe'}else{'--manual-desktop-probe'}
$taskProcess=Start-Process -FilePath $taskExecutable -ArgumentList ($taskMode+' '+$Seconds) -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
$taskStart=$taskProcess.StartTime.ToUniversalTime().Ticks;$taskForced=$false;$taskRemaining=$null;$taskExit=$null
Write-Output ('ManualDesktopPrivateDirectory='+$taskRun)
try {
  $taskClock=[Diagnostics.Stopwatch]::StartNew()
  while(-not $taskProcess.WaitForExit(1000)){
    if($taskClock.Elapsed.TotalSeconds -gt $Seconds+10){
      [IO.File]::WriteAllText((Join-Path $taskRun 'manual-exit.request'),'exit')
      if(-not $taskProcess.WaitForExit(10000)){throw 'Bounded input window did not respond to its own exit request.'}
    }
  }
  $taskExit=$taskProcess.ExitCode
} finally {
  try {
    if(-not $taskProcess.HasExited){
      if($taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart -or -not $taskProcess.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase)){throw 'Owned input process identity changed; refusing cleanup.'}
      $taskProcess.Kill();$taskForced=$true
      if(-not $taskProcess.WaitForExit(5000)){throw 'Own input process cleanup was not confirmed.'}
    }
    $taskCleanup=[Diagnostics.Stopwatch]::StartNew()
    do {
      # Unknown profile matches are only reported, never adopted or killed.
      $taskRemaining=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -OperationTimeoutSec 5 | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskRun)}).Count
      if($taskRemaining -eq 0){break};Start-Sleep -Milliseconds 200
    }while($taskCleanup.ElapsedMilliseconds -lt 15000)
    $taskResult=[pscustomobject]@{mode='bounded-desktop-input';normalExit=($taskExit -eq 0 -and -not $taskForced -and $taskRemaining -eq 0);
      exitCode=$taskExit;forcedCleanup=$taskForced;remainingDedicatedProcesses=$taskRemaining;unattended=[bool]$UnattendedTimeoutCheck;nativeInputAccepted=$false;outputDirectory=$taskRun}
    $taskResult | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'session-result.json')
    if(-not $taskResult.normalExit){throw 'Manual input session did not exit normally; evidence is incomplete.'}
    $taskDesktop=Get-Content -LiteralPath (Join-Path $taskRun 'desktop-summary.json') -Raw | ConvertFrom-Json
    if(-not $taskDesktop.passed -or $taskDesktop.remainingLeases -ne 0 -or $taskDesktop.attachments -lt 1 -or $taskDesktop.attachments -ne $taskDesktop.detachments){throw 'Manual desktop attachment did not cleanly finish.'}
    if($UnattendedTimeoutCheck){
      $taskSession=Get-Content (Join-Path $taskRun 'manual-session.json') -Raw | ConvertFrom-Json
      if(-not $taskSession.presentationShown -or -not $taskSession.inputPanelReady -or $taskSession.value -ne 0 -or $taskSession.sequence -ne 0 -or -not $taskSession.unattended -or $taskSession.endReason -ne 'deadline' -or $taskSession.elapsedSeconds -lt $Seconds){throw 'Unattended deadline was not observed.'}
    }
  } finally {$taskProcess.Dispose()}
}
