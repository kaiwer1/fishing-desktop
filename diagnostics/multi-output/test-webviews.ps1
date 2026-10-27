param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$CapturedTopology)
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory -CapturedTopology:$CapturedTopology
$taskRun=$taskBuild.outputDirectory
$taskProcess=$null;$taskStart=$null;$taskForced=$false;$taskFailure=$null;$taskCleanupConfirmed=$false;$taskExit=$null
try{
    $taskArgument=if($CapturedTopology){'--captured-topology-self-test'}else{'--self-test'}
    $taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList $taskArgument -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
    try{$taskStart=$taskProcess.StartTime.ToUniversalTime().Ticks;if(-not $taskProcess.Path.Equals($taskBuild.executable,[StringComparison]::OrdinalIgnoreCase)){throw 'Just-launched host path differs.'}}
    catch{
        # This is the exact Process object returned by the immediate launch,
        # not a later lookup by name or a guessed reused PID.
        if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own just-launched host did not close.'}}
        throw
    }
    if(-not $taskProcess.WaitForExit(60000)){throw 'Logical multi-output checks exceeded their deadline.'}
    $taskExit=$taskProcess.ExitCode
    if($taskExit -ne 0){throw ('Logical multi-output host exited '+$taskExit+'. Private output: '+$taskRun)}
    if($CapturedTopology){
        $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'captured-topology-summary.json') -Raw | ConvertFrom-Json
        $taskCount=$taskReport.currentOutputCount
        if(-not $taskReport.passed -or $taskReport.mode -ne 'captured-topology' -or $taskReport.checks.Count -ne 12 -or $taskCount -lt 1 -or $taskCount -gt 4 -or $taskReport.maximumOutputs -ne 4 -or
           $taskReport.simultaneousWebViews -ne ($taskCount+1) -or $taskReport.loadedDocuments -ne (2*$taskCount+1) -or $taskReport.hostViewGeneration -ne (2*$taskCount) -or
           $taskReport.sequence -ne 2 -or $taskReport.value -ne 10 -or -not $taskReport.singleOwner -or -not $taskReport.realWebViewMessages -or -not $taskReport.explicitNativeTestGrant -or
           -not $taskReport.topologyCaptureIntegrated -or -not $taskReport.topologyBridgeIntegrated -or $taskReport.actualKnownCaptures -ne 3 -or $taskReport.injectedEnumerationFailures -ne 1 -or
           $taskReport.captureSerial -ne 4 -or $taskReport.topologyGeneration -ne 2 -or -not $taskReport.recoverySameContent -or
           $taskReport.windowsCreated -ne (6*$taskCount) -or $taskReport.windowsReleased -ne $taskReport.windowsCreated -or $taskReport.outstandingWindows -ne 0 -or -not $taskReport.nativeWindowCleanupConfirmed -or
           -not $taskReport.hiddenNoActivateProtocolWindowsVerified -or -not $taskReport.fullscreenObservationSynthetic -or -not $taskReport.rendererObservationSynthetic -or -not $taskReport.landscapeObservationFromCapturedBounds -or
           -not $taskReport.outputKeysAreCaptureAliases -or -not $taskReport.currentDeviceOnly -or -not $taskReport.normalCompletion -or
           $taskReport.physicalMultiMonitorAcceptance -or $taskReport.mixedDpiAcceptance -or $taskReport.displaySettingsChanged -or $taskReport.externalWindowsQueried -or $taskReport.contentCaptured -or
           $taskReport.desktopAttachmentTested -or $taskReport.nativeInputTested -or $taskReport.gameLoaded -or $taskReport.clockIntegrated -or $taskReport.sdkRendererPolicyIntegrated){throw 'Captured topology protocol evidence mismatch.'}
        $taskSamples=@(Get-Content -LiteralPath (Join-Path $taskRun 'captured-topology-observations.json') -Raw | ConvertFrom-Json)
        if($taskSamples.Count -ne 4 -or $taskSamples[0].captureSerial -ne 1 -or -not $taskSamples[0].known -or $taskSamples[0].applied -ne 'Replaced' -or
           $taskSamples[1].captureSerial -ne 2 -or -not $taskSamples[1].known -or $taskSamples[1].applied -ne 'Unchanged' -or $taskSamples[1].topologyGeneration -ne 1 -or
           $taskSamples[2].captureSerial -ne 3 -or $taskSamples[2].known -or $taskSamples[2].reason -ne 'EnumerationFailed' -or $taskSamples[2].outputs.Count -ne 0 -or $taskSamples[2].applied -ne 'Unavailable' -or
           $taskSamples[3].captureSerial -ne 4 -or -not $taskSamples[3].known -or $taskSamples[3].applied -ne 'Replaced' -or $taskSamples[3].topologyGeneration -ne 2){throw 'Captured topology observation sequence mismatch.'}
    }else{
      $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'multi-output-summary.json') -Raw | ConvertFrom-Json
      if(-not $taskReport.passed -or $taskReport.checks.Count -ne 15 -or $taskReport.logicalOutputCount -ne 2 -or $taskReport.simultaneousWebViews -ne 3 -or
       $taskReport.loadedDocuments -ne 6 -or $taskReport.sequence -ne 2 -or $taskReport.value -ne 10 -or -not $taskReport.singleOwner -or
       -not $taskReport.realWebViewMessages -or -not $taskReport.explicitNativeTestGrant -or -not $taskReport.logicalOutputsOnly -or
       -not $taskReport.outputPolicyIntegrated -or -not $taskReport.syntheticObservationsOnly -or $taskReport.semanticGlobalMappingsVerified -ne 7 -or $taskReport.hostViewGeneration -ne 5 -or
       $taskReport.topologyGeneration -ne 1 -or $taskReport.inputEpoch -le 0 -or $taskReport.controlTarget -ne 'right' -or -not $taskReport.policyCanRun -or $taskReport.controlEligible -or
       $taskReport.nativeInputTested -or $taskReport.desktopAttachmentTested -or $taskReport.physicalMonitorsTested -or $taskReport.dpiChanged -or $taskReport.gameLoaded){throw 'Logical multi-output evidence mismatch.'}
    }
}catch{$taskFailure=$_}
finally{
    try{
        if($null -ne $taskProcess -and -not $taskProcess.HasExited){
            if($null -eq $taskStart -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart -or
               -not $taskProcess.Path.Equals($taskBuild.executable,[StringComparison]::OrdinalIgnoreCase)){throw 'Own host identity unavailable or changed; refusing broad cleanup.'}
            $taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own logical-output host did not exit.'}
        }
        $taskWait=[Diagnostics.Stopwatch]::StartNew()
        do{
            $taskRemaining=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -OperationTimeoutSec 5 | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskRun)})
            if($taskRemaining.Count -eq 0){break};Start-Sleep -Milliseconds 100
        }while($taskWait.ElapsedMilliseconds -lt 15000)
        if($taskRemaining.Count -gt 0){throw 'Private profile processes remain; refusing broad cleanup.'}
        $taskCleanupConfirmed=$true
        if($taskForced -and -not $taskFailure){throw 'Logical output checks required forced cleanup.'}
    }catch{if(-not $taskFailure){$taskFailure=$_}}
    finally{if($null -ne $taskProcess){$taskProcess.Dispose()}}
}
$taskResult=[pscustomobject]@{passed=($null -eq $taskFailure);exitCode=$taskExit;forcedCleanup=$taskForced;cleanupConfirmed=$taskCleanupConfirmed;
    remainingDedicatedProcesses=$(if($taskCleanupConfirmed){0}else{$null});logicalOutputsOnly=(-not [bool]$CapturedTopology);capturedTopologyMode=[bool]$CapturedTopology;outputDirectory=$taskRun;result=$(if($taskFailure){$null}else{$taskReport})}
$taskResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'process-summary.json') -Encoding utf8
if($taskFailure){throw $taskFailure}
$taskResult | ConvertTo-Json -Depth 6
