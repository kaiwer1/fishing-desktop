<#
.SYNOPSIS
Captures PresentMon 2.6.0 data for one proven Lively WebView2 GPU process.
.DESCRIPTION
Requires an already running, unique exact Lively host and one matching wallpaper
player/GPU child. Does not start a wallpaper, request elevation, use hotkeys, alter
services, or stop another trace. Empty/failed captures are unavailable, not 0 FPS.
All executable paths, process identities, CSV and logs remain in a new private
output directory outside the repository. PresentMon options follow its 2.6.0 help.
#>
param(
  [Parameter(Mandatory=$true)][string]$LivelyExecutable,
  [Parameter(Mandatory=$true)][string]$PresentMonExecutable,
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [ValidateRange(5,60)][int]$Seconds=30
)
$ErrorActionPreference='Stop'
$taskLively=(Resolve-Path -LiteralPath $LivelyExecutable).Path
$taskPresentMon=(Resolve-Path -LiteralPath $PresentMonExecutable).Path
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if(-not (Test-Path -LiteralPath $taskLively -PathType Leaf) -or [IO.Path]::GetFileName($taskLively) -ine 'Lively.exe'){throw 'Specify the exact Lively.exe host.'}
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
$taskPlayerPath=Join-Path (Split-Path -Parent $taskLively) 'Plugins/WebView2/Lively.Player.WebView2.exe'
if(-not (Test-Path -LiteralPath $taskPlayerPath -PathType Leaf)){throw 'The expected Lively WebView2 player was not found beside this host.'}
$taskPlayerPath=(Resolve-Path -LiteralPath $taskPlayerPath).Path
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
    $taskProcess.Refresh()
    if($taskProcess.HasExited -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart.Ticks){return $null}
    return [pscustomobject]@{id=[int]$taskMetadata.ProcessId;parentId=[int]$taskMetadata.ParentProcessId;
      startTicks=$taskStart.Ticks;startTimeUtc=$taskStart.ToString('o');path=$taskMetadata.ExecutablePath;
      name=$taskMetadata.Name;playerAncestor=$null;gpuFlag=($taskMetadata.CommandLine -match '(?:^|\s)"?--type=gpu-process"?(?=\s|$)')}
  }catch{return $null}
  finally{if($null -ne $taskProcess){$taskProcess.Dispose()}}
}
function Same-Identity($left,$right){return $null -ne $left -and $null -ne $right -and $left.id -eq $right.id -and $left.startTicks -eq $right.startTicks}
function Find-CaptureTarget {
  # Enumeration is internal only; no unrelated process or command-line data is saved.
  $taskMetadata=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,ExecutablePath,Name,CommandLine -OperationTimeoutSec 5)
  $taskRoots=@($taskMetadata | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.Equals($taskLively,[StringComparison]::OrdinalIgnoreCase)})
  if($taskRoots.Count -ne 1){throw 'Expected exactly one running instance of the requested Lively host.'}
  $taskRoot=Read-ProcessIdentity $taskRoots[0]
  if($null -eq $taskRoot){throw 'Lively root identity could not be verified.'}
  $taskById=@{};$taskByParent=@{}
  foreach($taskRow in $taskMetadata){
    $taskById[[int]$taskRow.ProcessId]=$taskRow
    $taskParent=[int]$taskRow.ParentProcessId
    if(-not $taskByParent.ContainsKey($taskParent)){$taskByParent[$taskParent]=[Collections.Generic.List[object]]::new()}
    $taskByParent[$taskParent].Add($taskRow)
  }
  $taskQueue=[Collections.Generic.Queue[object]]::new();$taskQueue.Enqueue($taskRoot)
  $taskAccepted=@{};$taskAccepted[$taskRoot.id]=$taskRoot
  while($taskQueue.Count -gt 0){
    $taskParent=$taskQueue.Dequeue()
    foreach($taskChildMetadata in @($taskByParent[$taskParent.id])){
      if($null -eq $taskChildMetadata -or $taskAccepted.ContainsKey([int]$taskChildMetadata.ProcessId)){continue}
      $taskChild=Read-ProcessIdentity $taskChildMetadata
      if($null -eq $taskChild -or $taskChild.startTicks -lt $taskParent.startTicks){continue}
      if(-not (Same-Identity $taskParent (Read-ProcessIdentity $taskById[$taskParent.id]))){throw 'A parent changed during capture-target discovery.'}
      $taskChild.playerAncestor=$taskParent.playerAncestor
      if($taskChild.path.Equals($taskPlayerPath,[StringComparison]::OrdinalIgnoreCase)){$taskChild.playerAncestor=$taskChild.id}
      $taskAccepted[$taskChild.id]=$taskChild;$taskQueue.Enqueue($taskChild)
    }
  }
  $taskPlayers=@($taskAccepted.Values | Where-Object {$_.path.Equals($taskPlayerPath,[StringComparison]::OrdinalIgnoreCase)})
  if($taskPlayers.Count -ne 1){throw 'Expected one proven wallpaper WebView2 player; no broad capture is allowed.'}
  $taskTargets=@($taskAccepted.Values | Where-Object {$_.playerAncestor -eq $taskPlayers[0].id -and $_.name -ieq 'msedgewebview2.exe' -and $_.gpuFlag})
  if($taskTargets.Count -ne 1){throw 'Expected one proven wallpaper GPU process; zero or ambiguous targets are refused.'}
  return [pscustomobject]@{root=$taskRoot;player=$taskPlayers[0];gpu=$taskTargets[0]}
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
  rawCsvFile=$taskCsv;scope='Only the one proven wallpaper WebView2 GPU PID; not the whole system.'}
$taskMetadata | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'metadata.json') -Encoding utf8
$taskRechecked=Find-CaptureTarget
foreach($taskPart in @('root','player','gpu')){
  if(-not (Same-Identity $taskTarget.$taskPart $taskRechecked.$taskPart)){throw 'Capture target changed before launch; capture was not started.'}
}

function Stop-OwnedProcess($process,[long]$startTicks){
  if($null -eq $process -or $process.HasExited){return}
  if($process.StartTime.ToUniversalTime().Ticks -ne $startTicks){throw 'Owned helper identity changed; refusing to stop it.'}
  try{$process.Kill()}catch [InvalidOperationException]{if(-not $process.HasExited){throw}}
  if(-not $process.WaitForExit(5000)){throw 'Owned helper has not exited.'}
}
$taskProcess=$null;$taskProcessStart=0;$taskExit=$null;$taskReason=$null;$taskTimedOut=$false
$taskLaunched=$false;$taskCleanupExit=$null;$taskCleanupError=$null;$taskSessionAfter=$null
$taskStarted=[DateTime]::UtcNow;$taskTimer=[Diagnostics.Stopwatch]::StartNew()
try{
  $taskProcess=Start-Process -FilePath $taskPresentMon -ArgumentList $taskArguments -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $taskRun 'presentmon.stdout.txt') -RedirectStandardError (Join-Path $taskRun 'presentmon.stderr.txt')
  $taskLaunched=$true;$taskProcessStart=$taskProcess.StartTime.ToUniversalTime().Ticks
  while(-not $taskProcess.WaitForExit(250)){
    if($taskTimer.Elapsed.TotalSeconds -ge $Seconds+15){$taskTimedOut=$true;$taskReason='capture-timeout';break}
  }
  if(-not $taskTimedOut){$taskExit=$taskProcess.ExitCode;if($taskExit -ne 0){$taskReason='presentmon-nonzero-exit'}}
}catch{$taskReason='capture-launch-or-wait-failed'}
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
          $taskStopperStart=$taskStopper.StartTime.ToUniversalTime().Ticks
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
  $taskTimer.Stop()
}

$taskRows=0;$taskCsvBytes=0;$taskIdentityAtEnd=$false
try{
  $taskEndTarget=Find-CaptureTarget
  $taskIdentityAtEnd=(Same-Identity $taskTarget.root $taskEndTarget.root) -and (Same-Identity $taskTarget.player $taskEndTarget.player) -and (Same-Identity $taskTarget.gpu $taskEndTarget.gpu)
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
  csvRowCount=$taskRows;csvBytes=$taskCsvBytes;rawCsvFile=$taskCsv;metadataFile=(Join-Path $taskRun 'metadata.json');
  cleanup=[pscustomobject]@{ownedSessionName=$taskSession;queryReturnCode=$taskSessionAfter;sessionAbsent=($taskSessionAfter -eq 4201);helperExitCode=$taskCleanupExit;error=$taskCleanupError};
  fps=$null;limits=@('No FPS is calculated by this capture script. Empty CSV or tool failure is unavailable, not zero FPS.',
    'One GPU process may contain multiple swapchains; display/swapchain attribution must be reviewed before reporting frame rates.',
    'Input and GPU-work tracking are disabled. No elevation, global hotkey, existing trace stop, service change or wallpaper input is performed.')}
$taskReport | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 6
