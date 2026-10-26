<#
.SYNOPSIS
Captures PresentMon 2.6.0 data for one proven host WebView2 GPU process.
.DESCRIPTION
Requires an already running unique exact Lively or DualWindowHost owner and one
proven GPU child. Owner capture also verifies its private browser profile.
Does not start a wallpaper, request elevation, use hotkeys, alter
services, or stop another trace. Empty/failed captures are unavailable, not 0 FPS.
All executable paths, process identities, CSV and logs remain in a new private
output directory outside the repository. PresentMon options follow its 2.6.0 help.
#>
[CmdletBinding(DefaultParameterSetName='Lively')]
param(
  [Parameter(Mandatory=$true,ParameterSetName='Lively')][string]$LivelyExecutable,
  [Parameter(Mandatory=$true,ParameterSetName='Owner')][string]$OwnerExecutable,
  [Parameter(Mandatory=$true)][string]$PresentMonExecutable,
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [ValidateRange(5,60)][int]$Seconds=30
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'present-target.ps1')
$taskKind=$PSCmdlet.ParameterSetName
$taskRequested=if($taskKind -eq 'Owner'){$OwnerExecutable}else{$LivelyExecutable}
$taskExpectedName=if($taskKind -eq 'Owner'){'DualWindowHost.exe'}else{'Lively.exe'}
$taskExecutable=(Resolve-Path -LiteralPath $taskRequested).Path
$taskPresentMon=(Resolve-Path -LiteralPath $PresentMonExecutable).Path
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf) -or [IO.Path]::GetFileName($taskExecutable) -ine $taskExpectedName){throw ('Specify the exact '+$taskExpectedName+' host.')}
if(-not (Test-Path -LiteralPath $taskPresentMon -PathType Leaf)){throw 'PresentMonExecutable must be a file.'}
if(-not (Test-Path -LiteralPath $taskOutput -PathType Container)){throw 'OutputDirectory must be an existing directory.'}
if($taskOutput.Equals($taskRepository,[StringComparison]::OrdinalIgnoreCase) -or $taskOutput.StartsWith($taskRepository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Capture output must remain outside the repository.'}
# Resolve-Path alone does not establish containment through junctions. Refuse them
# for this private output location instead of potentially writing into the repo.
$taskAncestor=Get-Item -LiteralPath $taskOutput
while($null -ne $taskAncestor){
  if(($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'OutputDirectory must use a direct path without reparse-point ancestors.'}
  $taskAncestor=$taskAncestor.Parent
}
$taskPlayerPath=$null;$taskOwnerProfile=$null
if($taskKind -eq 'Lively'){
  $taskPlayerPath=Join-Path (Split-Path -Parent $taskExecutable) 'Plugins/WebView2/Lively.Player.WebView2.exe'
  if(-not (Test-Path -LiteralPath $taskPlayerPath -PathType Leaf)){throw 'The expected Lively WebView2 player was not found beside this host.'}
  $taskPlayerPath=(Resolve-Path -LiteralPath $taskPlayerPath).Path
}else{
  $taskOwnerProfile=(Resolve-Path -LiteralPath (Join-Path (Split-Path -Parent $taskExecutable) 'private-profile')).Path
  $taskProfileAncestor=Get-Item -LiteralPath $taskOwnerProfile
  if(-not $taskProfileAncestor.PSIsContainer){throw 'Owner profile must be an existing private directory.'}
  while($null -ne $taskProfileAncestor){
    if(($taskProfileAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Owner profile must use a direct path without reparse points.'}
    $taskProfileAncestor=$taskProfileAncestor.Parent
  }
  $taskRuntimeProfile=Join-Path $taskOwnerProfile 'EBWebView'
  if(Test-Path -LiteralPath $taskRuntimeProfile){
    $taskRuntimeItem=Get-Item -LiteralPath $taskRuntimeProfile
    if(-not $taskRuntimeItem.PSIsContainer -or ($taskRuntimeItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'The owner runtime profile must be a direct directory.'}
  }
}
$taskHash=(Get-FileHash -LiteralPath $taskPresentMon -Algorithm SHA256).Hash.ToLowerInvariant()
$taskSignature=Get-AuthenticodeSignature -LiteralPath $taskPresentMon
# Official v2.6.0 x64 release asset, independently verified before this script.
if($taskHash -ne 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af' -or $taskSignature.Status -ne 'Valid'){
  throw 'The signed, verified PresentMon 2.6.0 x64 binary is required.'
}

function Read-ProcessIdentity($taskMetadata){
  $taskProcess=$null
  try{
    if($null -eq $taskMetadata -or -not $taskMetadata.ExecutablePath -or $null -eq $taskMetadata.CreationDate){return $null}
    $taskProcess=Get-Process -Id ([int]$taskMetadata.ProcessId) -ErrorAction Stop
    $taskStart=$taskProcess.StartTime.ToUniversalTime()
    if([Math]::Abs(($taskMetadata.CreationDate.ToUniversalTime()-$taskStart).TotalMilliseconds) -gt 2){return $null}
    if(-not $taskProcess.Path.Equals($taskMetadata.ExecutablePath,[StringComparison]::OrdinalIgnoreCase)){return $null}
    if([IO.Path]::GetFileName([string]$taskMetadata.ExecutablePath) -ine [string]$taskMetadata.Name){return $null}
    $taskProcess.Refresh()
    if($taskProcess.HasExited -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart.Ticks){return $null}
    return [pscustomobject]@{id=[int]$taskMetadata.ProcessId;parentId=[int]$taskMetadata.ParentProcessId;
      startTicks=$taskStart.Ticks;startTimeUtc=$taskStart.ToString('o');path=$taskMetadata.ExecutablePath;
      name=$taskMetadata.Name;command=[string]$taskMetadata.CommandLine}
  }catch{return $null}
  finally{if($null -ne $taskProcess){$taskProcess.Dispose()}}
}
function Same-Identity($left,$right){return Same-PresentIdentity $left $right}
function Find-CaptureTarget {
  # Enumeration is internal only; no unrelated process or command-line data is saved.
  $taskMetadata=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,ExecutablePath,Name,CommandLine -OperationTimeoutSec 5)
  return Get-PresentCaptureTarget -Rows $taskMetadata -RootExecutable $taskExecutable -Kind $taskKind -PlayerExecutable $taskPlayerPath `
    -OwnerProfileDirectory $taskOwnerProfile -ReadIdentity ${function:Read-ProcessIdentity}
}

# Query only the exact generated session. This helper exposes no start/stop API.
# https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-controltracew
if(-not ('FishingDesktopPresentTraceQuery' -as [type])){
  Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
public static class FishingDesktopPresentTraceQuery {
  [StructLayout(LayoutKind.Sequential)] struct Wnode {
    public uint BufferSize,ProviderId; public ulong HistoricalContext; public long TimeStamp;
    public Guid Guid; public uint ClientContext,Flags;
  }
  [StructLayout(LayoutKind.Sequential)] struct Properties {
    public Wnode Wnode;
    public uint BufferSize,MinimumBuffers,MaximumBuffers,MaximumFileSize,LogFileMode,FlushTimer,EnableFlags;
    public int AgeLimit;
    public uint NumberOfBuffers,FreeBuffers,EventsLost,BuffersWritten,LogBuffersLost,RealTimeBuffersLost;
    public IntPtr LoggerThreadId; public uint LogFileNameOffset,LoggerNameOffset;
  }
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
  static extern uint ControlTraceW(ulong handle,string name,IntPtr properties,uint control);
  public static uint Query(string name) {
    if(!Regex.IsMatch(name,"^FishingDesktop-[a-f0-9]{32}$"))throw new ArgumentException("Invalid owned session name.");
    int size=Marshal.SizeOf(typeof(Properties)), names=1025*2, length=size+names*2;
    IntPtr buffer=Marshal.AllocHGlobal(length);
    try {
      Marshal.Copy(new byte[length],0,buffer,length);
      var p=new Properties();p.Wnode.BufferSize=(uint)length;p.Wnode.Flags=0x00020000;
      p.LoggerNameOffset=(uint)size;p.LogFileNameOffset=(uint)(size+names);
      Marshal.StructureToPtr(p,buffer,false);
      return ControlTraceW(0,name,buffer,0); // EVENT_TRACE_CONTROL_QUERY only
    } finally {Marshal.FreeHGlobal(buffer);}
  }
}
'@
}

$taskTarget=Find-CaptureTarget
$taskSession='FishingDesktop-'+[Guid]::NewGuid().ToString('N')
$taskBeforeSession=[FishingDesktopPresentTraceQuery]::Query($taskSession)
if($taskBeforeSession -ne 4201){throw 'The unique trace name was not confirmed absent; capture was not started.'}
$taskRun=Join-Path $taskOutput ('present-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCsv=Join-Path $taskRun 'presents.csv'
$taskArguments=@('--process_id',[string]$taskTarget.gpu.id,'--timed',[string]$Seconds,'--terminate_after_timed',
  '--terminate_on_proc_exit','--session_name',$taskSession,'--no_console_stats','--no_track_input','--no_track_gpu',
  '--v2_metrics','--write_display_metadata','--output_file',('"'+$taskCsv+'"'))
$taskMetadata=[pscustomobject]@{createdAtUtc=[DateTime]::UtcNow.ToString('o');requestedSeconds=$Seconds;
  target=$taskTarget;sessionName=$taskSession;sessionAbsentBeforeCapture=$true;presentMonExecutable=$taskPresentMon;
  presentMonSha256=$taskHash;signatureStatus=[string]$taskSignature.Status;arguments=$taskArguments;
  hostKind=$taskKind;hostExecutable=$taskExecutable;ownerProfileVerified=$taskTarget.ownerProfileVerified;
  rawCsvFile=$taskCsv;scope='Only the one proven host WebView2 GPU PID; not the whole system.'}
$taskMetadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'metadata.json') -Encoding utf8
$taskRechecked=Find-CaptureTarget
$taskIdentityParts=if($taskKind -eq 'Owner'){@('root','browser','gpu')}else{@('root','player','gpu')}
foreach($taskPart in $taskIdentityParts){
  if(-not (Same-Identity $taskTarget.$taskPart $taskRechecked.$taskPart)){throw 'Capture target changed before launch; capture was not started.'}
}

$script:taskCaptureStage='before-helper-launch'
$script:taskHelperObservedPath=$null
function Write-PrivateCaptureError($taskError,[string]$taskName='capture-error.json'){
  # Private diagnostic only: no command lines, ETW payloads or exception message
  # text. Paths here refer only to this verified PresentMon binary.
  try{
    [pscustomobject]@{stage=$script:taskCaptureStage;exceptionType=$taskError.Exception.GetType().FullName;
      hresult=$taskError.Exception.HResult;expectedHelperPath=$taskPresentMon;observedHelperPath=$script:taskHelperObservedPath;
      observedPathAvailable=([bool]$script:taskHelperObservedPath);
      pathOrdinalIgnoreCaseEqual=($script:taskHelperObservedPath -and $script:taskHelperObservedPath.Equals($taskPresentMon,[StringComparison]::OrdinalIgnoreCase))} |
      ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $taskRun $taskName) -Encoding utf8
  }catch{}
}
function Stop-OwnedProcess($process,[long]$startTicks){
  if($null -eq $process -or $process.HasExited){return}
  if($process.StartTime.ToUniversalTime().Ticks -ne $startTicks -or -not $process.Path.Equals($taskPresentMon,[StringComparison]::OrdinalIgnoreCase)){throw 'Owned helper identity changed; refusing to stop it.'}
  try{$process.Kill()}catch [InvalidOperationException]{if(-not $process.HasExited){throw}}
  if(-not $process.WaitForExit(5000)){throw 'Owned helper has not exited.'}
}
function Read-OwnedHelperStart($process){
  try{
    $script:taskCaptureStage='helper-read-path'
    $script:taskHelperObservedPath=$process.Path
    if(-not $script:taskHelperObservedPath -or -not $script:taskHelperObservedPath.Equals($taskPresentMon,[StringComparison]::OrdinalIgnoreCase)){throw 'The just-launched helper path could not be confirmed.'}
    $script:taskCaptureStage='helper-read-start-time'
    return $process.StartTime.ToUniversalTime().Ticks
  }catch{
    Write-PrivateCaptureError $_ 'helper-identity-error.json'
    # Use only the Process object returned by this immediate launch, never a
    # fresh PID/name lookup, if initial metadata cannot be collected.
    if(-not $process.HasExited){$process.Kill();if(-not $process.WaitForExit(5000)){throw 'The just-launched helper did not close.'}}
    throw
  }
}
$taskProcess=$null;$taskProcessStart=0;$taskExit=$null;$taskReason=$null;$taskTimedOut=$false
$taskGpuLease=$null
$taskLaunched=$false;$taskCleanupExit=$null;$taskCleanupError=$null;$taskSessionAfter=$null
$taskStarted=[DateTime]::UtcNow;$taskTimer=[Diagnostics.Stopwatch]::StartNew()
try{
  $script:taskCaptureStage='verify-target-gpu-handle'
  # Retain the verified target's native process handle throughout the trace,
  # after checking its start time and path immediately before helper launch.
  $taskGpuLease=Get-Process -Id $taskTarget.gpu.id -ErrorAction Stop
  if($taskGpuLease.StartTime.ToUniversalTime().Ticks -ne $taskTarget.gpu.startTicks -or
     -not $taskGpuLease.Path.Equals($taskTarget.gpu.path,[StringComparison]::OrdinalIgnoreCase)){throw 'GPU identity changed before launch.'}
  [void]$taskGpuLease.Handle
  if($taskGpuLease.HasExited){throw 'GPU exited before capture launch.'}
  $script:taskCaptureStage='launch-presentmon'
  $taskProcess=Start-Process -FilePath $taskPresentMon -ArgumentList $taskArguments -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $taskRun 'presentmon.stdout.txt') -RedirectStandardError (Join-Path $taskRun 'presentmon.stderr.txt')
  $taskLaunched=$true;$taskProcessStart=Read-OwnedHelperStart $taskProcess
  $script:taskCaptureStage='wait-presentmon'
  while(-not $taskProcess.WaitForExit(250)){
    if($taskTimer.Elapsed.TotalSeconds -ge $Seconds+15){$taskTimedOut=$true;$taskReason='capture-timeout';break}
  }
  if(-not $taskTimedOut){$taskExit=$taskProcess.ExitCode;if($taskExit -ne 0){$taskReason='presentmon-nonzero-exit'}}
}catch{Write-PrivateCaptureError $_;$taskReason='capture-launch-or-wait-failed'}
finally{
  try{Stop-OwnedProcess $taskProcess $taskProcessStart}catch{$taskCleanupError='owned-capture-process-exit-unconfirmed'}
  if($null -ne $taskProcess){
    if($taskProcess.HasExited -and $null -eq $taskExit){$taskExit=$taskProcess.ExitCode}
    $taskProcess.Dispose()
  }
  if($taskLaunched){
    try{
      $taskSessionAfter=[FishingDesktopPresentTraceQuery]::Query($taskSession)
      if($taskSessionAfter -ne 4201){
        # This name was absent before OUR launch. Never pass stop_existing_session,
        # a default session name, a different PID, restart_as_admin, or a hotkey.
        $taskStopper=$null;$taskStopperStart=0
        try{
          $taskStopper=Start-Process -FilePath $taskPresentMon -ArgumentList @('--session_name',$taskSession,'--terminate_existing_session','--no_console_stats') `
            -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun 'cleanup.stdout.txt') -RedirectStandardError (Join-Path $taskRun 'cleanup.stderr.txt')
          $taskStopperStart=Read-OwnedHelperStart $taskStopper
          if($taskStopper.WaitForExit(10000)){$taskCleanupExit=$taskStopper.ExitCode}
        }finally{
          Stop-OwnedProcess $taskStopper $taskStopperStart
          if($null -ne $taskStopper){$taskStopper.Dispose()}
        }
        $taskCleanupTimer=[Diagnostics.Stopwatch]::StartNew()
        do{
          $taskSessionAfter=[FishingDesktopPresentTraceQuery]::Query($taskSession)
          if($taskSessionAfter -eq 4201){break}
          Start-Sleep -Milliseconds 100
        }while($taskCleanupTimer.ElapsedMilliseconds -lt 5000)
      }
      if($taskSessionAfter -ne 4201){$taskCleanupError='owned-session-removal-unconfirmed'}
    }catch{$taskCleanupError='owned-session-cleanup-failed'}
  }
  if($null -ne $taskGpuLease){$taskGpuLease.Dispose()}
  $taskTimer.Stop()
}

$taskRows=0;$taskCsvBytes=0;$taskIdentityAtEnd=$false
try{
  $taskEndTarget=Find-CaptureTarget
  $taskIdentityAtEnd=$true
  foreach($taskPart in $taskIdentityParts){if(-not (Same-Identity $taskTarget.$taskPart $taskEndTarget.$taskPart)){$taskIdentityAtEnd=$false}}
}catch{}
if(-not $taskIdentityAtEnd -and -not $taskReason){$taskReason='target-exited-or-changed'}
if(Test-Path -LiteralPath $taskCsv -PathType Leaf){
  $taskCsvBytes=(Get-Item -LiteralPath $taskCsv).Length
  try{
    if($taskCsvBytes -gt 128MB){throw 'CSV size exceeds diagnostic bound.'}
    Import-Csv -LiteralPath $taskCsv | ForEach-Object {
      $taskRowId=0
      if(-not [int]::TryParse($_.ProcessID,[ref]$taskRowId) -or $taskRowId -ne $taskTarget.gpu.id){throw 'CSV scope mismatch.'}
      $taskRows++
    }
  }catch{if(-not $taskReason){$taskReason='csv-invalid-or-scope-mismatch'}}
}
if($taskRows -eq 0 -and -not $taskReason){$taskReason='no-present-records'}
if($taskCleanupError -and -not $taskReason){$taskReason=$taskCleanupError}
$taskReport=[pscustomobject]@{status=$(if($null -eq $taskReason){'captured'}else{'unavailable'});reason=$taskReason;
  startedAtUtc=$taskStarted.ToString('o');elapsedSeconds=$taskTimer.Elapsed.TotalSeconds;requestedSeconds=$Seconds;
  toolExitCode=$taskExit;timedOut=$taskTimedOut;targetIdentityStillMatched=$taskIdentityAtEnd;
  hostKind=$taskKind;ownerProfileVerified=$taskTarget.ownerProfileVerified;
  csvRowCount=$taskRows;csvBytes=$taskCsvBytes;rawCsvFile=$taskCsv;metadataFile=(Join-Path $taskRun 'metadata.json');
  cleanup=[pscustomobject]@{ownedSessionName=$taskSession;queryReturnCode=$taskSessionAfter;sessionAbsent=($taskSessionAfter -eq 4201);helperExitCode=$taskCleanupExit;error=$taskCleanupError};
  fps=$null;limits=@('No FPS is calculated by this capture script. Empty CSV or tool failure is unavailable, not zero FPS.',
    'One GPU process may contain multiple swapchains; display/swapchain attribution must be reviewed before reporting frame rates.',
    'Input and GPU-work tracking are disabled. No elevation, global hotkey, existing trace stop, service change or wallpaper input is performed.')}
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 6
