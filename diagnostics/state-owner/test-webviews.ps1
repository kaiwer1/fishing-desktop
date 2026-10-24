param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$taskRoot=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if($taskRoot.Equals($taskRepo,[StringComparison]::OrdinalIgnoreCase) -or $taskRoot.StartsWith($taskRepo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Private output must be outside the repository.'}
$taskRun=Join-Path $taskRoot ('owner-webview-run-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -SdkDirectory $SdkDirectory -OutputDirectory $taskRun
foreach($taskReportName in @('owner-summary.json','restart-summary.json')) {
  $taskProcess=Start-Process -FilePath (Join-Path $taskRun 'StateOwnerProbeHost.exe') -ArgumentList '--self-test' -WorkingDirectory $taskRun -WindowStyle Hidden -PassThru
  $taskFailure=$null
  try {
    if(-not $taskProcess.WaitForExit(35000)){throw 'Shared state WebView test timed out.'}
    if($taskProcess.ExitCode -ne 0){throw ("Shared state WebView test exited "+$taskProcess.ExitCode+"; private output: "+$taskRun)}
    $taskReport=Get-Content -LiteralPath (Join-Path $taskRun $taskReportName) -Raw | ConvertFrom-Json
    if(-not $taskReport.passed -or $taskReport.value -ne 10 -or $taskReport.sequence -ne 2){throw 'Shared state result mismatch.'}
  } catch { $taskFailure=$_ }
  finally {
    if(-not $taskProcess.HasExited){$taskProcess.Kill();$taskProcess.WaitForExit(5000)|Out-Null}
    $taskProcess.Dispose()
    try {
      $taskTimer=[Diagnostics.Stopwatch]::StartNew()
      do {
        $taskRemaining=@(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object {$_.CommandLine -and $_.CommandLine.Contains($taskRun)})
        if($taskRemaining.Count -eq 0){break}
        Start-Sleep -Milliseconds 100
      } while($taskTimer.ElapsedMilliseconds -lt 15000)
      if($taskRemaining.Count){throw 'Dedicated test WebViews have not exited.'}
    } catch {
      $_.Exception.Message | Set-Content -LiteralPath (Join-Path $taskRun 'cleanup-error.txt')
      if(-not $taskFailure){$taskFailure=$_}
    }
  }
  if($taskFailure){throw $taskFailure}
}
$taskFirst=Get-Content -LiteralPath (Join-Path $taskRun 'owner-summary.json') -Raw | ConvertFrom-Json
$taskSecond=Get-Content -LiteralPath (Join-Path $taskRun 'restart-summary.json') -Raw | ConvertFrom-Json
[pscustomobject]@{passed=$true;initialChecks=$taskFirst.checks;restartChecks=$taskSecond.checks;value=10;sequence=2;remainingDedicatedWebViews=0;gameLoaded=$false;nativeMouseTested=$false} | ConvertTo-Json -Depth 4 | Tee-Object -FilePath (Join-Path $taskRun 'summary.json')
