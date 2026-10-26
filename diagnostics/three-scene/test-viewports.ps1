param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot '../dual-window/build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory -ThreeScene
$taskRun=$taskBuild.outputDirectory
$taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList '--three-viewport-self-test' -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
$taskStart=$taskProcess.StartTime.ToUniversalTime().Ticks
$taskForced=$false;$taskFailure=$null;$taskCleanupConfirmed=$false;$taskExit=$null
try{
    if(-not $taskProcess.WaitForExit(75000)){throw 'Virtual viewport checks exceeded their outer deadline.'}
    $taskExit=$taskProcess.ExitCode
    if($taskExit -ne 0){throw ('Virtual viewport host exited '+$taskExit+'. Private output: '+$taskRun)}
    $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'viewport-summary.json') -Raw | ConvertFrom-Json
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 32 -or $taskReport.viewports.Count -ne 32 -or
      -not $taskReport.virtualViewportsOnly -or -not $taskReport.controlledDraws -or -not $taskReport.browserZoomOnly -or $taskReport.physicalDisplayChanged -or $taskReport.dpiChanged -or
      $taskReport.multiplePhysicalMonitorsTested -or $taskReport.presentedFpsMeasured -or $taskReport.nativeInputTested -or $taskReport.gameLoaded -or
      $taskReport.sequence -ne 0 -or $taskReport.value -ne 0 -or -not $taskReport.presentationChecksPassed){throw 'Virtual viewport report mismatch.'}
}catch{$taskFailure=$_}
finally{
    try{
        if(-not $taskProcess.HasExited){
            if($taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart -or -not $taskProcess.Path.Equals($taskBuild.executable,[StringComparison]::OrdinalIgnoreCase)){throw 'Viewport process identity changed; refusing cleanup.'}
            $taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own viewport process did not close.'}
        }
        $taskCleanup=[Diagnostics.Stopwatch]::StartNew()
        do{
            $taskRemaining=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -OperationTimeoutSec 5 | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskRun)})
            if($taskRemaining.Count -eq 0){break};Start-Sleep -Milliseconds 100
        }while($taskCleanup.ElapsedMilliseconds -lt 15000)
        if($taskRemaining.Count -gt 0){throw 'Unknown dedicated-profile processes remain; refusing broad cleanup.'}
        $taskCleanupConfirmed=$true
        if($taskForced -and -not $taskFailure){throw 'Viewport check required forced cleanup.'}
    }catch{if(-not $taskFailure){$taskFailure=$_}}
    finally{$taskProcess.Dispose()}
}
$taskResult=[pscustomobject]@{passed=($null -eq $taskFailure);exitCode=$taskExit;forcedCleanup=$taskForced;cleanupConfirmed=$taskCleanupConfirmed;
    remainingDedicatedProcesses=$(if($taskCleanupConfirmed){0}else{$null});virtualViewportsOnly=$true;outputDirectory=$taskRun;result=$(if($taskFailure){$null}else{$taskReport})}
$taskResult | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $taskRun 'viewport-process-summary.json') -Encoding utf8
if($taskFailure){throw $taskFailure}
$taskResult | ConvertTo-Json -Depth 7
