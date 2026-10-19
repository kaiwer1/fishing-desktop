param(
  [Parameter(Mandatory=$true)][string]$SdkDirectory,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRun=Join-Path $taskRoot ('instance-run-'+[Guid]::NewGuid().ToString('N'))
$taskA=Join-Path $taskRun 'build-a'
$taskB=Join-Path $taskRun 'build-b'
New-Item -ItemType Directory -Path $taskA,$taskB -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskA | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskB | Out-Null
$taskProcesses=[Collections.Generic.List[Diagnostics.Process]]::new()
$taskChecks=[Collections.Generic.List[string]]::new()

function Start-Probe([string]$folder) {
  $taskProcess=Start-Process -FilePath (Join-Path $folder 'InputProbeHost.exe') -ArgumentList '--instance-probe' -WorkingDirectory $folder -WindowStyle Hidden -PassThru
  $taskProcesses.Add($taskProcess)
  return $taskProcess
}
function Read-Probe([Diagnostics.Process]$process,[string]$folder) {
  $taskPath=Join-Path $folder ('instance-probe.'+$process.Id+'.json')
  $taskWait=[Diagnostics.Stopwatch]::StartNew()
  while($taskWait.Elapsed.TotalSeconds -lt 5) {
    if(Test-Path -LiteralPath $taskPath) {
      try { return (Get-Content -LiteralPath $taskPath -Raw | ConvertFrom-Json) } catch { }
    }
    if($process.HasExited){throw 'Probe exited before reporting ownership.'}
    Start-Sleep -Milliseconds 50
  }
  throw 'Probe readiness timed out.'
}
function Assert-Owner([Diagnostics.Process]$process,[string]$folder,[bool]$expected) {
  $taskValue=Read-Probe $process $folder
  if($taskValue.pid -ne $process.Id -or $taskValue.owner -ne $expected){throw 'Unexpected ownership result.'}
}
function Assert-Rejected([Diagnostics.Process]$process,[string]$folder) {
  Assert-Owner $process $folder $false
  if(-not $process.WaitForExit(5000) -or $process.ExitCode -ne 2){throw 'Duplicate instance did not exit with rejection.'}
}

try {
  $taskFirst=Start-Probe $taskA
  Assert-Owner $taskFirst $taskA $true
  $taskChecks.Add('first process owns gate')
  $taskDuplicate=Start-Probe $taskA
  Assert-Rejected $taskDuplicate $taskA
  $taskChecks.Add('same-directory duplicate rejected')
  $taskOtherBuild=Start-Probe $taskB
  Assert-Rejected $taskOtherBuild $taskB
  $taskChecks.Add('other build directory rejected')

  New-Item -ItemType File -Path (Join-Path $taskA ('instance-release.'+$taskFirst.Id+'.signal')) | Out-Null
  if(-not $taskFirst.WaitForExit(5000) -or $taskFirst.ExitCode -ne 0){throw 'Owner did not release normally.'}
  $taskRecovered=Start-Probe $taskB
  Assert-Owner $taskRecovered $taskB $true
  $taskChecks.Add('new process owns gate after normal release')

  $taskRecovered.Kill()
  if(-not $taskRecovered.WaitForExit(5000)){throw 'Test owner did not exit.'}
  $taskAfterCrash=Start-Probe $taskA
  Assert-Owner $taskAfterCrash $taskA $true
  $taskChecks.Add('new process owns gate after forced termination')
  New-Item -ItemType File -Path (Join-Path $taskA ('instance-release.'+$taskAfterCrash.Id+'.signal')) | Out-Null
  if(-not $taskAfterCrash.WaitForExit(5000) -or $taskAfterCrash.ExitCode -ne 0){throw 'Final owner did not release.'}
  $taskReport=[pscustomobject]@{passed=$true;checks=$taskChecks.ToArray();headless=$true;gameLoaded=$false}
  $taskReport | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
  $taskReport | ConvertTo-Json -Depth 4
}
finally {
  foreach($taskProcess in $taskProcesses) {
    if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskProcess.WaitForExit(5000) | Out-Null}
    $taskProcess.Dispose()
  }
}
