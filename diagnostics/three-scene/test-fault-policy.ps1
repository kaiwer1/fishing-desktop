param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[ValidateSet('','silent-stop','stale-flag','no-response','transition-pending','record-failure')][string]$WatchdogCase='')
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot '../dual-window/build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory -ThreeScene
$taskRun=$taskBuild.outputDirectory
$taskOwned=@();$taskForced=$false;$taskCleanupConfirmed=$false;$taskFailure=$null;$taskExit=$null;$taskRestartExit=$null
function Start-SceneFaultHost([string[]]$Mode){
    $taskStarted=Start-Process -FilePath $taskBuild.executable -ArgumentList $Mode -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
    try{return [pscustomobject]@{Process=$taskStarted;StartTicks=$taskStarted.StartTime.ToUniversalTime().Ticks}}
    catch{
        # Keep the exact Process returned by this launch; do not find or kill
        # a process by name when initial metadata collection itself fails.
        try{if(-not $taskStarted.HasExited){$taskStarted.Kill();$script:taskForced=$true;if(-not $taskStarted.WaitForExit(5000)){throw 'Own just-launched process did not close.'}}}
        finally{$taskStarted.Dispose()}
        throw
    }
}
function Wait-SceneFaultProfile {
    $taskWait=[Diagnostics.Stopwatch]::StartNew()
    do{
        $taskLeft=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -OperationTimeoutSec 5 | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskRun)})
        if($taskLeft.Count -eq 0){return};Start-Sleep -Milliseconds 100
    }while($taskWait.ElapsedMilliseconds -lt 15000)
    throw 'Private profile processes remain; refusing broad cleanup.'
}
try{
    $taskMode=if($WatchdogCase){@('--three-watchdog-self-test',$WatchdogCase)}else{@('--three-fault-policy-self-test')}
    $taskFaultHost=Start-SceneFaultHost -Mode $taskMode;$taskOwned+=,$taskFaultHost
    if(-not $taskFaultHost.Process.WaitForExit(30000)){throw 'Scene failure policy exceeded its deadline.'}
    $taskExit=$taskFaultHost.Process.ExitCode
    if($taskExit -ne 4){throw ('Expected conservative failure exit 4; observed '+$taskExit)}
    Wait-SceneFaultProfile
    $taskPreflight=Get-Content -LiteralPath (Join-Path $taskRun 'scene-fault-preflight.json') -Raw | ConvertFrom-Json
    $taskFault=Get-Content -LiteralPath (Join-Path $taskRun 'scene-fault-exit.json') -Raw | ConvertFrom-Json
    $taskReason=if($WatchdogCase -in @('silent-stop','record-failure')){'draw-loop-stopped'}elseif($WatchdogCase -eq 'stale-flag'){'draw-progress-stalled'}elseif($WatchdogCase -eq 'no-response'){'draw-response-timeout'}elseif($WatchdogCase -eq 'transition-pending'){'draw-renderer-transition-timeout'}else{'webgl-context-lost'}
    $taskModeEvidence=if($WatchdogCase){$taskPreflight.watchdogCase -eq $WatchdogCase -and $taskPreflight.pauseLongerThanGraceVerified -and
        $taskPreflight.reloadVerified -and $taskPreflight.watchdogSamples -ge 3 -and -not $taskPreflight.ownContextExtensionOnly -and
        $taskFault.origin -eq 'watchdog' -and $taskFault.watchdogSamples -ge 3}else{$taskPreflight.invalidReportsIgnored -eq 6 -and
        $taskPreflight.ignoredReportsAcknowledged -and $taskPreflight.ownContextExtensionOnly -and $taskFault.origin -eq 'page'}
    if(-not $taskPreflight.passed -or $taskPreflight.checks.Count -ne 5 -or -not $taskModeEvidence -or
       -not $taskPreflight.selectiveRendererEnabled -or
       $taskPreflight.desktopAttachmentTested -or $taskPreflight.hardwareDeviceLossTested -or $taskPreflight.gameLoaded -or
       $taskFault.schema -ne 1 -or $taskFault.reason -ne $taskReason -or -not $taskFault.currentReadOnlyDocument -or
       $taskFault.canRun -or $taskFault.reasons -notmatch 'SceneFault' -or -not $taskFault.oldCommandRejectedByPause -or
       $taskFault.sequence -ne 1 -or $taskFault.value -ne 7 -or -not $taskFault.explicitRestartRequired){throw 'Scene fault evidence mismatch.'}
    if([bool]$taskFault.recordWriteFailed -ne ($WatchdogCase -eq 'record-failure') -or
       ($WatchdogCase -eq 'record-failure' -and (-not $taskPreflight.faultRecordPathBlocked -or -not (Test-Path -LiteralPath (Join-Path $taskRun 'scene-fault.json') -PathType Container))) -or
       ($WatchdogCase -eq 'transition-pending' -and -not $taskPreflight.syntheticPendingBackend)){throw 'Injected failure boundary mismatch.'}
    $taskRestart=Start-SceneFaultHost '--scene-fault-restart-check';$taskOwned+=,$taskRestart
    if(-not $taskRestart.Process.WaitForExit(10000)){throw 'Explicit counter restart check exceeded its deadline.'}
    $taskRestartExit=$taskRestart.Process.ExitCode
    if($taskRestartExit -ne 0){throw ('Restart verification failed '+$taskRestartExit)}
    $taskRecovered=Get-Content -LiteralPath (Join-Path $taskRun 'scene-fault-restart.json') -Raw | ConvertFrom-Json
    if(-not $taskRecovered.passed -or -not $taskRecovered.newProcess -or -not $taskRecovered.confirmedStatePreserved -or
       -not $taskRecovered.initialStartupBlocked -or -not $taskRecovered.oldLeaseRejected -or -not $taskRecovered.explicitNewLeaseRequired -or
       -not $taskRecovered.duplicateReceiptPreserved -or $taskRecovered.sequence -ne 1 -or $taskRecovered.value -ne 7 -or $taskRecovered.gameLoaded){throw 'Explicit restart evidence mismatch.'}
}catch{$taskFailure=$_}
finally{
    try{
        foreach($taskIdentity in $taskOwned){
            $taskProcess=$taskIdentity.Process
            if(-not $taskProcess.HasExited){
                if($taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskIdentity.StartTicks -or
                   -not $taskProcess.Path.Equals($taskBuild.executable,[StringComparison]::OrdinalIgnoreCase)){throw 'Own process identity changed; refusing cleanup.'}
                $taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own scene process did not close.'}
            }
        }
        Wait-SceneFaultProfile;$taskCleanupConfirmed=$true
        if($taskForced -and -not $taskFailure){throw 'Scene policy required forced cleanup.'}
    }catch{if(-not $taskFailure){$taskFailure=$_}}
    finally{foreach($taskIdentity in $taskOwned){$taskIdentity.Process.Dispose()}}
}
$taskResult=[pscustomobject]@{passed=($null -eq $taskFailure);expectedFailureExitCode=4;observedFailureExitCode=$taskExit;restartExitCode=$taskRestartExit;
    forcedCleanup=$taskForced;cleanupConfirmed=$taskCleanupConfirmed;remainingDedicatedProcesses=$(if($taskCleanupConfirmed){0}else{$null});
    ownContextExtensionOnly=(-not [bool]$WatchdogCase);watchdogCase=$WatchdogCase;hardwareDeviceLossTested=$false;desktopAttachmentTested=$false;gameLoaded=$false;outputDirectory=$taskRun;
    preflight=$(if($taskFailure){$null}else{$taskPreflight | Select-Object passed,checks,invalidReportsIgnored,ignoredReportsAcknowledged,selectiveRendererEnabled,watchdogCase,pauseLongerThanGraceVerified,reloadVerified,watchdogSamples});
    fault=$(if($taskFailure){$null}else{$taskFault | Select-Object reason,origin,canRun,reasons,oldCommandRejectedByPause,watchdogSamples,watchdogRequests,watchdogLateIgnored,recordWriteFailed,sequence,value,explicitRestartRequired});
    restart=$(if($taskFailure){$null}else{$taskRecovered})}
$taskResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'scene-fault-process-summary.json') -Encoding utf8
if($taskFailure){throw $taskFailure}
$taskResult | ConvertTo-Json -Depth 6
