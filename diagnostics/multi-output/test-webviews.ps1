param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskBuild=& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $OutputDirectory
$taskRun=$taskBuild.outputDirectory
$taskProcess=$null;$taskStart=$null;$taskForced=$false;$taskFailure=$null;$taskCleanupConfirmed=$false;$taskExit=$null
try{
    $taskProcess=Start-Process -FilePath $taskBuild.executable -ArgumentList '--self-test' -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
    try{$taskStart=$taskProcess.StartTime.ToUniversalTime().Ticks}
    catch{
        # This is the exact Process object returned by the immediate launch,
        # not a later lookup by name or a guessed reused PID.
        if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own just-launched host did not close.'}}
        throw
    }
    if(-not $taskProcess.WaitForExit(60000)){throw 'Logical multi-output checks exceeded their deadline.'}
    $taskExit=$taskProcess.ExitCode
    if($taskExit -ne 0){throw ('Logical multi-output host exited '+$taskExit+'. Private output: '+$taskRun)}
    $taskReport=Get-Content -LiteralPath (Join-Path $taskRun 'multi-output-summary.json') -Raw | ConvertFrom-Json
    if(-not $taskReport.passed -or $taskReport.checks.Count -ne 10 -or $taskReport.logicalOutputCount -ne 2 -or $taskReport.simultaneousWebViews -ne 3 -or
       $taskReport.loadedDocuments -ne 6 -or $taskReport.sequence -ne 2 -or $taskReport.value -ne 10 -or -not $taskReport.singleOwner -or
       -not $taskReport.realWebViewMessages -or -not $taskReport.explicitNativeTestGrant -or -not $taskReport.logicalOutputsOnly -or
       $taskReport.nativeInputTested -or $taskReport.desktopAttachmentTested -or $taskReport.physicalMonitorsTested -or $taskReport.dpiChanged -or $taskReport.gameLoaded){throw 'Logical multi-output evidence mismatch.'}
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
    remainingDedicatedProcesses=$(if($taskCleanupConfirmed){0}else{$null});logicalOutputsOnly=$true;outputDirectory=$taskRun;result=$(if($taskFailure){$null}else{$taskReport})}
$taskResult | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'process-summary.json') -Encoding utf8
if($taskFailure){throw $taskFailure}
$taskResult | ConvertTo-Json -Depth 6
