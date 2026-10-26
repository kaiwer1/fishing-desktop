[CmdletBinding(DefaultParameterSetName='Lively')]
param(
  [Parameter(Mandatory=$true,ParameterSetName='Lively')][string]$LivelyExecutable,
  [Parameter(Mandatory=$true,ParameterSetName='Owner')][string]$OwnerExecutable,
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [ValidateRange(5,7200)][int]$Seconds = 30,
  [ValidateRange(1,60)][int]$IntervalSeconds = 1,
  [ValidateSet('active','paused','unlabelled')][string]$Phase = 'unlabelled'
)
$ErrorActionPreference='Stop'
function Assert-DirectOutputDirectory([string]$taskPath){
  # Resolve-Path alone does not prove containment through a junction. Reject
  # reparse points at the output directory and every ancestor before any output.
  $taskAncestor=Get-Item -LiteralPath $taskPath
  while($null -ne $taskAncestor){
    if(($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){
      throw 'OutputDirectory must use a direct path without reparse-point ancestors.'
    }
    $taskAncestor=$taskAncestor.Parent
  }
}
if($IntervalSeconds -gt $Seconds){throw 'IntervalSeconds must not exceed Seconds.'}
$taskExpectedName=if($PSCmdlet.ParameterSetName -eq 'Owner'){'DualWindowHost.exe'}else{'Lively.exe'}
$taskRequestedExecutable=if($PSCmdlet.ParameterSetName -eq 'Owner'){$OwnerExecutable}else{$LivelyExecutable}
$taskExecutable=(Resolve-Path -LiteralPath $taskRequestedExecutable).Path
if(-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf)){throw 'The requested host executable must be a file.'}
if([IO.Path]::GetFileName($taskExecutable) -ine $taskExpectedName){throw ('Specify the intended '+$taskExpectedName+' host.')}
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
if(-not (Test-Path -LiteralPath $taskOutput -PathType Container)){throw 'OutputDirectory must be an existing directory.'}
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
if($taskOutput.Equals($taskRepository,[StringComparison]::OrdinalIgnoreCase) -or $taskOutput.StartsWith($taskRepository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
  throw 'Performance records must stay outside the source repository.'
}
Assert-DirectOutputDirectory $taskOutput

# No host/window is started or changed. Enumeration remains internal; only the
# exact requested host and proven descendants are written to the private report.
$taskRootCandidates=@(Get-CimInstance Win32_Process -Filter "Name='$taskExpectedName'" -Property ProcessId,CreationDate,ExecutablePath -OperationTimeoutSec 5 | Where-Object {
  $_.ExecutablePath -and $_.ExecutablePath.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase)
})
if($taskRootCandidates.Count -ne 1){throw 'Expected exactly one running instance of the specified host executable.'}
$taskRootId=[int]$taskRootCandidates[0].ProcessId
$taskRootProcess=Get-Process -Id $taskRootId
try{$taskRootStart=$taskRootProcess.StartTime.ToUniversalTime()}
finally{$taskRootProcess.Dispose()}
if($null -eq $taskRootCandidates[0].CreationDate -or [Math]::Abs(($taskRootCandidates[0].CreationDate.ToUniversalTime()-$taskRootStart).TotalMilliseconds) -gt 2){
  throw 'The root process identity changed during discovery.'
}
$taskTracked=@{}
$taskTracked[$taskRootId]=[pscustomobject]@{id=$taskRootId;startTicks=$taskRootStart.Ticks;parentId=$null}
$taskPrevious=@{}
$taskEngineClass='Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine'
$taskMemoryClass='Win32_PerfFormattedData_GPUPerformanceCounters_GPUProcessMemory'
$taskClassInfo=@{}
foreach($taskClassName in @($taskEngineClass,$taskMemoryClass)){
  try{
    $taskClass=Get-CimClass -Namespace root/cimv2 -ClassName $taskClassName -OperationTimeoutSec 5
    $taskClassInfo[$taskClassName]=[pscustomobject]@{available=$true;properties=@($taskClass.CimClassProperties | ForEach-Object {$_.Name});errorType=$null}
  }catch{
    $taskClassInfo[$taskClassName]=[pscustomobject]@{available=$false;properties=@();errorType=$_.Exception.GetType().FullName}
  }
}
$taskRun=Join-Path $taskOutput ('gpu-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskRawPath=Join-Path $taskRun 'samples.jsonl'
$taskUtf8=[Text.UTF8Encoding]::new($false)

function Read-ScopedProcess($taskMetadata,$taskExpectedStartTicks){
  $taskProcess=$null
  try{
    $taskProcess=Get-Process -Id ([int]$taskMetadata.ProcessId) -ErrorAction Stop
    $taskStart=$taskProcess.StartTime.ToUniversalTime()
    if(($null -ne $taskExpectedStartTicks -and $taskStart.Ticks -ne $taskExpectedStartTicks) -or
       $null -eq $taskMetadata.CreationDate -or
       [Math]::Abs(($taskMetadata.CreationDate.ToUniversalTime()-$taskStart).TotalMilliseconds) -gt 2){return $null}
    $taskResult=[pscustomobject]@{
      id=[int]$taskMetadata.ProcessId
      parentId=[int]$taskMetadata.ParentProcessId
      processName=[IO.Path]::GetFileName([string]$taskMetadata.Name)
      isRoot=([int]$taskMetadata.ProcessId -eq $taskRootId -and $taskStart.Ticks -eq $taskRootStart.Ticks)
      identity=([string]$taskMetadata.ProcessId+'|'+[string]$taskStart.Ticks)
      startTimeUtc=$taskStart.ToString('o')
      startTicks=$taskStart.Ticks
      cpuSeconds=$taskProcess.TotalProcessorTime.TotalSeconds
      cpuSampleTicks=[Diagnostics.Stopwatch]::GetTimestamp()
      privateBytes=$taskProcess.PrivateMemorySize64
      workingSetBytes=$taskProcess.WorkingSet64
    }
    $taskProcess.Refresh()
    if($taskProcess.HasExited -or $taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart.Ticks){return $null}
    return $taskResult
  }catch{return $null}
  finally{if($null -ne $taskProcess){$taskProcess.Dispose()}}
}

function Get-ScopedSnapshot {
  try{
    $taskMetadata=@(Get-CimInstance Win32_Process -Property ProcessId,ParentProcessId,CreationDate,ExecutablePath,Name -OperationTimeoutSec 5)
  }catch{
    return [pscustomobject]@{rootAlive=$false;reason='process-enumeration-unavailable';errorType=$_.Exception.GetType().FullName;rows=@()}
  }
  $taskById=@{}; $taskByParent=@{}
  foreach($taskItem in $taskMetadata){
    $taskById[[int]$taskItem.ProcessId]=$taskItem
    $taskParent=[int]$taskItem.ParentProcessId
    if(-not $taskByParent.ContainsKey($taskParent)){$taskByParent[$taskParent]=[Collections.Generic.List[object]]::new()}
    $taskByParent[$taskParent].Add($taskItem)
  }
  $taskRootMetadata=$taskById[$taskRootId]
  if($null -eq $taskRootMetadata -or -not $taskRootMetadata.ExecutablePath -or
     -not $taskRootMetadata.ExecutablePath.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase)){
    return [pscustomobject]@{rootAlive=$false;reason='root-exited-or-path-unavailable';errorType=$null;rows=@()}
  }
  $taskRootSample=Read-ScopedProcess $taskRootMetadata $taskRootStart.Ticks
  if($null -eq $taskRootSample){
    return [pscustomobject]@{rootAlive=$false;reason='root-exited-or-identity-unavailable';errorType=$null;rows=@()}
  }
  $taskAccepted=@{}; $taskQueue=[Collections.Generic.Queue[object]]::new()
  # Retain descendants whose parent has already exited, but never adopt a reused PID.
  foreach($taskKnown in @($taskTracked.Values)){
    $taskCandidate=$taskById[$taskKnown.id]
    if($null -eq $taskCandidate -or $taskCandidate.Name -ieq 'dwm.exe'){continue}
    $taskSample=Read-ScopedProcess $taskCandidate $taskKnown.startTicks
    if($null -ne $taskSample){$taskAccepted[$taskSample.id]=$taskSample;$taskQueue.Enqueue($taskSample)}
  }
  if(-not $taskAccepted.ContainsKey($taskRootId)){
    return [pscustomobject]@{rootAlive=$false;reason='root-exited-during-scope-read';errorType=$null;rows=@()}
  }
  while($taskQueue.Count -gt 0){
    $taskParentSample=$taskQueue.Dequeue()
    foreach($taskChild in @($taskByParent[$taskParentSample.id])){
      if($null -eq $taskChild -or $taskChild.Name -ieq 'dwm.exe' -or $taskAccepted.ContainsKey([int]$taskChild.ProcessId)){continue}
      $taskChildSample=Read-ScopedProcess $taskChild $null
      if($null -eq $taskChildSample -or $taskChildSample.startTicks -lt $taskParentSample.startTicks){continue}
      # Recheck the parent after reading its child. Parent PID reuse must not create ancestry.
      if($null -eq (Read-ScopedProcess $taskById[$taskParentSample.id] $taskParentSample.startTicks)){
        if($taskParentSample.id -eq $taskRootId){
          return [pscustomobject]@{rootAlive=$false;reason='root-exited-or-identity-unavailable-during-parent-recheck';errorType=$null;rows=@()}
        }
        continue
      }
      $taskAccepted[$taskChildSample.id]=$taskChildSample
      $taskTracked[$taskChildSample.id]=[pscustomobject]@{id=$taskChildSample.id;startTicks=$taskChildSample.startTicks;parentId=$taskParentSample.id}
      $taskQueue.Enqueue($taskChildSample)
    }
  }
  # Descendant discovery may take time. Do not publish a valid scope when the
  # root has exited or its PID has been reused since the initial root read.
  $taskFinalRoot=Read-ScopedProcess $taskRootMetadata $taskRootStart.Ticks
  if($null -eq $taskFinalRoot){
    return [pscustomobject]@{rootAlive=$false;reason='root-exited-or-identity-unavailable-before-snapshot-return';errorType=$null;rows=@()}
  }
  $taskAccepted[$taskRootId]=$taskFinalRoot
  return [pscustomobject]@{rootAlive=$true;reason=$null;errorType=$null;rows=@($taskAccepted.Values | Sort-Object id)}
}

function Read-Provider([string]$taskClassName){
  # Initial schema discovery is evidence about that attempt, not a permanent
  # capability gate. A provider can recover after discovery or a prior sample.
  try{return [pscustomobject]@{available=$true;errorType=$null;rows=@(Get-CimInstance -Namespace root/cimv2 -ClassName $taskClassName -OperationTimeoutSec 5)}}
  catch{return [pscustomobject]@{available=$false;errorType=$_.Exception.GetType().FullName;rows=@()}}
}
function Counter-Value($taskInstance,[string]$taskField){
  $taskProperty=$taskInstance.CimInstanceProperties[$taskField]
  if($null -eq $taskProperty -or $null -eq $taskProperty.Value){return $null}
  return $taskProperty.Value
}
function Raw-Counters($taskInstance){
  $taskRaw=[ordered]@{}
  foreach($taskProperty in $taskInstance.CimInstanceProperties){
    if($taskProperty.Name -notin @('Name','Caption','Description')){$taskRaw[$taskProperty.Name]=$taskProperty.Value}
  }
  return $taskRaw
}
function Nullable-Stats($taskValues){
  $taskPresent=@($taskValues | Where-Object {$null -ne $_})
  if($taskPresent.Count -eq 0){return [pscustomobject]@{availableSamples=0;mean=$null;peak=$null}}
  $taskStats=$taskPresent | Measure-Object -Average -Maximum
  return [pscustomobject]@{availableSamples=$taskPresent.Count;mean=$taskStats.Average;peak=$taskStats.Maximum}
}
function Valid-CounterNumber($taskValue){
  if($null -eq $taskValue){return $false}
  if(@([byte],[sbyte],[short],[ushort],[int],[uint],[long],[ulong],[float],[double],[decimal]) -notcontains $taskValue.GetType()){return $false}
  return $taskValue -ge 0 -and -not [double]::IsNaN([double]$taskValue) -and -not [double]::IsInfinity([double]$taskValue)
}
function Sum-Counter($taskRows,[string]$taskField){
  $taskValues=@($taskRows | ForEach-Object {$_.$taskField} | Where-Object {Valid-CounterNumber $_})
  if($taskValues.Count -eq 0 -or $taskValues.Count -ne @($taskRows).Count){return $null}
  return ($taskValues | Measure-Object -Sum).Sum
}

$taskStarted=[DateTime]::UtcNow
$taskTimer=[Diagnostics.Stopwatch]::StartNew()
$taskSamples=[Collections.Generic.List[object]]::new()
$taskStopReason='duration-completed'
while($true){
  $taskSampleStart=$taskTimer.Elapsed.TotalSeconds
  $taskBefore=Get-ScopedSnapshot
  if(-not $taskBefore.rootAlive){$taskStopReason=$taskBefore.reason;break}
  $taskBeforeById=@{};foreach($taskRow in $taskBefore.rows){$taskBeforeById[$taskRow.id]=$taskRow}
  $taskEngineProvider=Read-Provider $taskEngineClass
  $taskMemoryProvider=Read-Provider $taskMemoryClass
  $taskAfter=Get-ScopedSnapshot
  if(-not $taskAfter.rootAlive){$taskStopReason=$taskAfter.reason;break}
  $taskStable=@{}
  foreach($taskRow in $taskAfter.rows){
    if($taskBeforeById.ContainsKey($taskRow.id) -and $taskBeforeById[$taskRow.id].startTicks -eq $taskRow.startTicks){$taskStable[$taskRow.id]=$taskRow}
  }
  $taskEngines=@()
  foreach($taskInstance in $taskEngineProvider.rows){
    if($taskInstance.Name -notmatch '^pid_(\d+)_(luid_0x[0-9a-fA-F]+_0x[0-9a-fA-F]+_phys_\d+)_eng_(\d+)_engtype_(.+)$'){continue}
    $taskId=[int]$Matches[1]
    if(-not $taskStable.ContainsKey($taskId)){continue}
    $taskEngines += [pscustomobject]@{
      processId=$taskId;processStartTimeUtc=$taskStable[$taskId].startTimeUtc
      instance=$taskInstance.Name;adapter=$Matches[2];engine=[int]$Matches[3];engineType=$Matches[4]
      engineKey=($Matches[2]+'_eng_'+$Matches[3]+'_engtype_'+$Matches[4])
      utilizationPercent=(Counter-Value $taskInstance 'UtilizationPercentage')
      runningTime=(Counter-Value $taskInstance 'RunningTime')
      timestampSys100Ns=(Counter-Value $taskInstance 'Timestamp_Sys100NS')
      rawCounters=(Raw-Counters $taskInstance)
    }
  }
  $taskMemory=@()
  foreach($taskInstance in $taskMemoryProvider.rows){
    if($taskInstance.Name -notmatch '^pid_(\d+)_(luid_0x[0-9a-fA-F]+_0x[0-9a-fA-F]+_phys_\d+)$'){continue}
    $taskId=[int]$Matches[1]
    if(-not $taskStable.ContainsKey($taskId)){continue}
    $taskMemory += [pscustomobject]@{
      processId=$taskId;processStartTimeUtc=$taskStable[$taskId].startTimeUtc;instance=$taskInstance.Name;adapter=$Matches[2]
      dedicatedBytes=(Counter-Value $taskInstance 'DedicatedUsage');sharedBytes=(Counter-Value $taskInstance 'SharedUsage')
      localBytes=(Counter-Value $taskInstance 'LocalUsage');nonLocalBytes=(Counter-Value $taskInstance 'NonLocalUsage')
      totalCommittedBytes=(Counter-Value $taskInstance 'TotalCommitted')
      timestampSys100Ns=(Counter-Value $taskInstance 'Timestamp_Sys100NS')
      rawCounters=(Raw-Counters $taskInstance)
    }
  }
  $taskGroups=@()
  foreach($taskGroup in @($taskEngines | Group-Object engineKey)){
    $taskValid=@($taskGroup.Group | Where-Object {Valid-CounterNumber $_.utilizationPercent})
    $taskGroupSum=$null
    if($taskValid.Count -eq $taskGroup.Count){$taskGroupSum=Sum-Counter $taskValid 'utilizationPercent'}
    $taskGroups += [pscustomobject]@{engineKey=$taskGroup.Name;processInstances=$taskGroup.Count;reportedPercentSum=$taskGroupSum}
  }
  $taskGpuMax=$null;$taskGpuSum=$null
  if($taskGroups.Count -gt 0 -and @($taskGroups | Where-Object {$null -eq $_.reportedPercentSum}).Count -eq 0){
    $taskGpuMax=($taskGroups | Measure-Object reportedPercentSum -Maximum).Maximum
    $taskGpuSum=Sum-Counter $taskGroups 'reportedPercentSum'
  }
  $taskProcessRows=@();$taskCpuRates=@()
  foreach($taskRow in $taskAfter.rows){
    $taskCpuRate=$null
    $taskEarlier=$taskPrevious[$taskRow.identity]
    if($null -ne $taskEarlier){
      $taskElapsed=($taskRow.cpuSampleTicks-$taskEarlier.cpuSampleTicks)/[double][Diagnostics.Stopwatch]::Frequency
      $taskDelta=$taskRow.cpuSeconds-$taskEarlier.cpuSeconds
      if($taskElapsed -gt 0 -and $taskDelta -ge 0){$taskCpuRate=100*$taskDelta/$taskElapsed;$taskCpuRates += $taskCpuRate}
    }
    $taskPrevious[$taskRow.identity]=$taskRow
    $taskOwnedMemory=@($taskMemory | Where-Object {$_.processId -eq $taskRow.id})
    $taskOwnedEngines=@($taskEngines | Where-Object {$_.processId -eq $taskRow.id})
    $taskNumericEngines=@($taskOwnedEngines | Where-Object {Valid-CounterNumber $_.utilizationPercent}).Count
    $taskNumericDedicated=@($taskOwnedMemory | Where-Object {Valid-CounterNumber $_.dedicatedBytes}).Count
    $taskNumericShared=@($taskOwnedMemory | Where-Object {Valid-CounterNumber $_.sharedBytes}).Count
    $taskProcessRows += [pscustomobject]@{
      id=$taskRow.id;parentId=$taskRow.parentId;startTimeUtc=$taskRow.startTimeUtc;identity=$taskRow.identity
      processName=$taskRow.processName;isRoot=$taskRow.isRoot
      cpuSeconds=$taskRow.cpuSeconds;cpuSampleTicks=$taskRow.cpuSampleTicks;cpuCoreEquivalentPercent=$taskCpuRate
      privateBytes=$taskRow.privateBytes;workingSetBytes=$taskRow.workingSetBytes
      identityStableAcrossGpuRead=$taskStable.ContainsKey($taskRow.id)
      gpuEngineInstanceCount=$taskOwnedEngines.Count;gpuMemoryInstanceCount=$taskOwnedMemory.Count
      gpuEngineNumericInstanceCount=$taskNumericEngines;gpuDedicatedNumericInstanceCount=$taskNumericDedicated;gpuSharedNumericInstanceCount=$taskNumericShared
      gpuEngineCompleteNumericCoverage=($taskOwnedEngines.Count -gt 0 -and $taskNumericEngines -eq $taskOwnedEngines.Count)
      gpuDedicatedCompleteNumericCoverage=($taskOwnedMemory.Count -gt 0 -and $taskNumericDedicated -eq $taskOwnedMemory.Count)
      gpuSharedCompleteNumericCoverage=($taskOwnedMemory.Count -gt 0 -and $taskNumericShared -eq $taskOwnedMemory.Count)
      reportedDedicatedBytes=(Sum-Counter $taskOwnedMemory 'dedicatedBytes')
      reportedSharedBytes=(Sum-Counter $taskOwnedMemory 'sharedBytes')
    }
  }
  $taskCpuTotal=$null;$taskCpuNormalized=$null
  if($taskCpuRates.Count -gt 0){$taskCpuTotal=($taskCpuRates | Measure-Object -Sum).Sum;$taskCpuNormalized=$taskCpuTotal/[Environment]::ProcessorCount}
  $taskSample=[pscustomobject]@{
    atUtc=[DateTime]::UtcNow.ToString('o');sampleStartElapsedSeconds=$taskSampleStart;sampleEndElapsedSeconds=$taskTimer.Elapsed.TotalSeconds
    phaseLabel=$Phase;processCount=$taskProcessRows.Count;cpuMatchedProcessCount=$taskCpuRates.Count
    cpuCoreEquivalentPercent=$taskCpuTotal;cpuMachineNormalizedPercent=$taskCpuNormalized
    privateBytes=(Sum-Counter $taskProcessRows 'privateBytes');summedWorkingSetBytes=(Sum-Counter $taskProcessRows 'workingSetBytes')
    gpuEngineProviderAvailable=$taskEngineProvider.available;gpuEngineErrorType=$taskEngineProvider.errorType
    gpuMemoryProviderAvailable=$taskMemoryProvider.available;gpuMemoryErrorType=$taskMemoryProvider.errorType
    gpuStableProcessCount=$taskStable.Count;gpuEngineProcessCount=@($taskEngines | Select-Object -ExpandProperty processId -Unique).Count
    gpuMemoryProcessCount=@($taskMemory | Select-Object -ExpandProperty processId -Unique).Count
    gpuEngineNumericProcessCount=@($taskProcessRows | Where-Object {$_.gpuEngineCompleteNumericCoverage}).Count
    gpuDedicatedNumericProcessCount=@($taskProcessRows | Where-Object {$_.gpuDedicatedCompleteNumericCoverage}).Count
    gpuSharedNumericProcessCount=@($taskProcessRows | Where-Object {$_.gpuSharedCompleteNumericCoverage}).Count
    gpuMemoryNumericProcessCount=@($taskProcessRows | Where-Object {$_.gpuDedicatedCompleteNumericCoverage -and $_.gpuSharedCompleteNumericCoverage}).Count
    maxSingleEngineReportedPercent=$taskGpuMax;sumAcrossEnginesReportedPercent=$taskGpuSum
    reportedDedicatedBytes=(Sum-Counter $taskMemory 'dedicatedBytes');reportedSharedBytes=(Sum-Counter $taskMemory 'sharedBytes')
    processes=$taskProcessRows;gpuEngines=$taskEngines;gpuEngineGroups=$taskGroups;gpuProcessMemory=$taskMemory
  }
  $taskSamples.Add($taskSample)
  [IO.File]::AppendAllText($taskRawPath,($taskSample | ConvertTo-Json -Depth 8 -Compress)+[Environment]::NewLine,$taskUtf8)
  if($taskTimer.Elapsed.TotalSeconds -ge $Seconds){break}
  $taskDelay=[Math]::Max(0,($taskSampleStart+$IntervalSeconds-$taskTimer.Elapsed.TotalSeconds)*1000)
  if($taskDelay -gt 0){Start-Sleep -Milliseconds ([int][Math]::Ceiling($taskDelay))}
}
$taskTimer.Stop()
$taskSummary=[pscustomobject]@{
  startedAtUtc=$taskStarted.ToString('o');phaseLabel=$Phase;requestedSeconds=$Seconds;elapsedSeconds=$taskTimer.Elapsed.TotalSeconds
  minimumSampleIntervalSeconds=$IntervalSeconds;sampleCount=$taskSamples.Count;stopReason=$taskStopReason
  rootProcessId=$taskRootId;rootStartTimeUtc=$taskRootStart.ToString('o');livelyExecutable=$taskExecutable
  ownerExecutable=$taskExecutable;ownerKind=$PSCmdlet.ParameterSetName
  logicalProcessors=[Environment]::ProcessorCount;counterClasses=$taskClassInfo
  cpuMachineNormalizedPercent=(Nullable-Stats @($taskSamples | ForEach-Object {$_.cpuMachineNormalizedPercent}))
  privateBytes=(Nullable-Stats @($taskSamples | ForEach-Object {$_.privateBytes}))
  maxSingleEngineReportedPercent=(Nullable-Stats @($taskSamples | ForEach-Object {$_.maxSingleEngineReportedPercent}))
  sumAcrossEnginesReportedPercent=(Nullable-Stats @($taskSamples | ForEach-Object {$_.sumAcrossEnginesReportedPercent}))
  reportedDedicatedBytes=(Nullable-Stats @($taskSamples | ForEach-Object {$_.reportedDedicatedBytes}))
  reportedSharedBytes=(Nullable-Stats @($taskSamples | ForEach-Object {$_.reportedSharedBytes}))
  rawSamplesFile=$taskRawPath
  limits=@(
    'Read-only sample of the exact requested host and proven descendants. DWM and unrelated processes are excluded.'
    'PID plus process start time is checked for CPU deltas and before/after GPU reads. GPU providers expose PID only; cached attribution cannot be fully verified.'
    'Missing counter instances/fields are null, not zero. Reported memory sums cover available instances only; inspect per-process coverage.'
    'counterClasses describes the initial schema discovery only. Each sample queries provider instances independently and records its own availability; a later successful query can recover from an earlier failure.'
    'Instance PID counts mean a matching counter instance exists. Numeric PID counts require valid nonnegative values in every matching instance for that metric; memory numeric coverage requires both dedicated and shared bytes.'
    'Maximum is the busiest single engine after summing scoped PID instances on that engine. Sum across engines can exceed 100 and is not overall GPU utilization.'
    'Dedicated/shared/local/non-local memory fields are bytes. Per-process GPU memory can count shared allocations repeatedly; summed values are not unique physical VRAM usage.'
    'CPU sums include processes present at two observations. Newly started or exited processes can be missed. Summed working sets can double count shared pages.'
    'Formatted GPU data has provider refresh timing and integer percentages; polling faster than provider refresh does not create additional precision.'
    'Summary means are arithmetic means of available samples, not time-weighted means. Provider queries can extend actual elapsed time beyond the requested interval.'
    'Phase is a caller label, not verified pause telemetry. No FPS, frame pacing, power, temperature, hardware capacity or long-term acceptance is measured.'
  )
  gpuInterpretationSource='https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/'
}
$taskSummaryPath=Join-Path $taskRun 'summary.json'
[IO.File]::WriteAllText($taskSummaryPath,($taskSummary | ConvertTo-Json -Depth 8),$taskUtf8)
$taskSummary | ConvertTo-Json -Depth 8
