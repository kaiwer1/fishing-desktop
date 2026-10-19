param(
  [Parameter(Mandatory=$true)][string]$LivelyExecutable,
  [Parameter(Mandatory=$true)][string]$OutputPath,
  [ValidateRange(5,30)][int]$Seconds = 10,
  [ValidateSet('active','paused')][string]$Phase = 'active'
)
$ErrorActionPreference = 'Stop'
$taskExecutable = (Resolve-Path -LiteralPath $LivelyExecutable).Path
$taskRoots = @(Get-CimInstance Win32_Process -Filter "Name='Lively.exe'" | Where-Object { $_.ExecutablePath -eq $taskExecutable })
if ($taskRoots.Count -ne 1) { throw 'Expected one instance of the specified Lively host.' }
$taskRootId = [int]$taskRoots[0].ProcessId

function Get-HostSnapshot {
  $taskQueue = [Collections.Generic.Queue[int]]::new()
  $taskSeen = [Collections.Generic.HashSet[int]]::new()
  $taskQueue.Enqueue($taskRootId)
  $taskRows = @()
  while ($taskQueue.Count -gt 0) {
    $taskId = $taskQueue.Dequeue()
    if (-not $taskSeen.Add($taskId)) { continue }
    $taskProcess = Get-Process -Id $taskId -ErrorAction SilentlyContinue
    if ($null -ne $taskProcess) {
      $taskRows += [pscustomobject]@{
        id = $taskId
        cpuSeconds = $taskProcess.TotalProcessorTime.TotalSeconds
        sampledAtTicks = [Diagnostics.Stopwatch]::GetTimestamp()
        privateBytes = $taskProcess.PrivateMemorySize64
        workingSetBytes = $taskProcess.WorkingSet64
      }
      foreach ($taskChild in @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$taskId")) {
        $taskQueue.Enqueue([int]$taskChild.ProcessId)
      }
    }
  }
  return $taskRows
}

$taskStartedAt = [DateTime]::UtcNow
$taskBefore = @(Get-HostSnapshot)
$taskTimer = [Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds $Seconds
$taskAfter = @(Get-HostSnapshot)
$taskTimer.Stop()
$taskCoreRate = 0.0
$taskMatched = 0
foreach ($taskEnd in $taskAfter) {
  $taskStart = $taskBefore | Where-Object { $_.id -eq $taskEnd.id } | Select-Object -First 1
  if ($null -ne $taskStart) {
    $taskInterval = ($taskEnd.sampledAtTicks - $taskStart.sampledAtTicks) / [double][Diagnostics.Stopwatch]::Frequency
    if ($taskInterval -gt 0) {
      $taskCoreRate += [Math]::Max(0, $taskEnd.cpuSeconds - $taskStart.cpuSeconds) / $taskInterval
    }
    $taskMatched++
  }
}
$taskResult = [pscustomobject]@{
  startedAt = $taskStartedAt.ToString('o')
  phaseLabel = $Phase # Caller label; verify actual pause with independent page telemetry.
  elapsedSeconds = [Math]::Round($taskTimer.Elapsed.TotalSeconds,3)
  matchedProcessCount = $taskMatched
  startProcessCount = $taskBefore.Count
  endProcessCount = $taskAfter.Count
  cpuCoreEquivalentPercent = [Math]::Round(100 * $taskCoreRate,2)
  cpuMachineNormalizedPercent = [Math]::Round(100 * $taskCoreRate / [Environment]::ProcessorCount,2)
  privateMiBAtEnd = [Math]::Round(($taskAfter | Measure-Object privateBytes -Sum).Sum / 1MB,2)
  summedWorkingSetMiBAtEnd = [Math]::Round(($taskAfter | Measure-Object workingSetBytes -Sum).Sum / 1MB,2)
  limits = 'Short sample of the specified host and descendants. CPU uses matching process IDs only; working sets may double count shared pages. No FPS/GPU/VRAM or long-term acceptance.'
}
$taskResult | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $OutputPath -Encoding utf8
$taskResult | ConvertTo-Json -Depth 4
