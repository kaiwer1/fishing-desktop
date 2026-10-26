$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'present-target.ps1')
$script:taskPassed=0;$script:taskFailed=0
function Need-PresentTest([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Reject-PresentTest([scriptblock]$Action){try{& $Action | Out-Null}catch{return};throw 'Expected target refusal.'}
function Test-PresentTarget([string]$Name,[scriptblock]$Action){
  try{& $Action;$script:taskPassed++;Write-Output ('PASS '+$Name)}
  catch{$script:taskFailed++;Write-Output ('FAIL '+$Name+': '+$_.Exception.Message)}
}
function New-PresentRow([int]$Id,[int]$Parent,[long]$Start,[string]$Path,[string]$Command=''){
  return [pscustomobject]@{ProcessId=$Id;ParentProcessId=$Parent;StartTicks=$Start;ExecutablePath=$Path;Name=[IO.Path]::GetFileName($Path);CommandLine=$Command}
}
function New-PresentFixture([string]$Kind='Owner'){
  $script:taskReads=@{};$script:taskMutation=$null
  if($Kind -eq 'Owner'){
    return @((New-PresentRow 10 1 100 'C:\app\DualWindowHost.exe'),
      (New-PresentRow 11 10 110 'C:\runtime\msedgewebview2.exe' '"C:\runtime\msedgewebview2.exe" --embedded-browser-webview=1 "--user-data-dir=C:\app\private-profile"'),
      (New-PresentRow 12 11 120 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe --type=gpu-process --user-data-dir="C:\app\private-profile"'))
  }
  return @((New-PresentRow 10 1 100 'C:\lively\Lively.exe'),
    (New-PresentRow 11 10 110 'C:\lively\Plugins\WebView2\Lively.Player.WebView2.exe'),
    (New-PresentRow 12 11 120 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe'),
    (New-PresentRow 13 12 130 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe --type=gpu-process'))
}
function Read-TestPresentIdentity($Row){
  if($null -eq $Row){return $null}
  $taskId=[int]$Row.ProcessId
  if(-not $script:taskReads.ContainsKey($taskId)){$script:taskReads[$taskId]=0};$script:taskReads[$taskId]++
  $taskIdentity=[pscustomobject]@{id=$taskId;parentId=[int]$Row.ParentProcessId;startTicks=[long]$Row.StartTicks;
    startTimeUtc=([DateTime]::new([long]$Row.StartTicks,[DateTimeKind]::Utc)).ToString('o');path=$Row.ExecutablePath;name=$Row.Name;command=$Row.CommandLine}
  if($null -ne $script:taskMutation){return & $script:taskMutation $taskIdentity $script:taskReads[$taskId]}
  return $taskIdentity
}
function Select-TestPresent($Rows,[string]$Kind='Owner'){
  $taskRoot=if($Kind -eq 'Owner'){'C:\app\DualWindowHost.exe'}else{'C:\lively\Lively.exe'}
  return Get-PresentCaptureTarget -Rows $Rows -RootExecutable $taskRoot -Kind $Kind -OwnerProfileDirectory 'C:\app\private-profile' `
    -PlayerExecutable 'C:\lively\Plugins\WebView2\Lively.Player.WebView2.exe' -ReadIdentity ${function:Read-TestPresentIdentity}
}
Test-PresentTarget 'owner captures only one proven private-profile browser GPU and omits raw commands' {
  $taskTarget=Select-TestPresent (New-PresentFixture)
  Need-PresentTest ($taskTarget.gpu.id -eq 12 -and $taskTarget.browser.id -eq 11 -and $taskTarget.ownerProfileVerified -and $null -eq $taskTarget.player) 'Wrong owner branch.'
  Need-PresentTest ($taskTarget.gpu.gpuFlag -and -not $taskTarget.root.gpuFlag -and $null -eq $taskTarget.gpu.playerAncestor) 'Owner derived role flags are inconsistent.'
  Need-PresentTest (-not ($taskTarget | ConvertTo-Json -Depth 6).Contains('embedded-browser')) 'Raw command line escaped scope selection.'
}
Test-PresentTarget 'Lively retains exact wallpaper player ancestry' {
  $taskTarget=Select-TestPresent (New-PresentFixture Lively) Lively
  Need-PresentTest ($taskTarget.player.id -eq 11 -and $taskTarget.gpu.id -eq 13 -and -not $taskTarget.ownerProfileVerified) 'Lively branch changed.'
  Need-PresentTest ($taskTarget.gpu.gpuFlag -and $taskTarget.player.playerAncestor -eq 11 -and $taskTarget.gpu.playerAncestor -eq 11) 'Legacy Lively role fields were not preserved.'
}
Test-PresentTarget 'unrelated GPU processes are neither adopted nor read' {
  $taskRows=New-PresentFixture;$taskRows+=New-PresentRow 88 1 200 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe --type=gpu-process --user-data-dir=C:\other'
  Need-PresentTest ((Select-TestPresent $taskRows).gpu.id -eq 12 -and -not $script:taskReads.ContainsKey(88)) 'Unrelated process was inspected or adopted.'
}
Test-PresentTarget 'multiple exact roots are refused' {
  $taskRows=New-PresentFixture;$taskRows+=New-PresentRow 30 1 200 'C:\app\DualWindowHost.exe'
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'missing exact root is refused' {
  $taskRows=New-PresentFixture;Reject-PresentTest {Select-TestPresent @($taskRows | Where-Object {$_.ProcessId -ne 10})}
}
Test-PresentTarget 'duplicate PID rows are refused' {
  $taskRows=New-PresentFixture;$taskRows+=$taskRows[2].PSObject.Copy();Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'two proven GPUs are refused rather than selecting the first' {
  $taskRows=New-PresentFixture;$taskRows+=New-PresentRow 15 11 125 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe --type=gpu-process'
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'no GPU is unavailable rather than an empty broad target' {
  $taskRows=New-PresentFixture;$taskRows[2].CommandLine='msedgewebview2.exe --type=renderer';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'external browser profile is refused' {
  $taskRows=New-PresentFixture;$taskRows[1].CommandLine='msedgewebview2.exe --user-data-dir=C:\other';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'runtime EBWebView child layout stays inside the exact owner profile' {
  $taskRows=New-PresentFixture
  $taskRows[1].CommandLine='msedgewebview2.exe --user-data-dir=C:\app\private-profile\EBWebView'
  $taskRows[2].CommandLine='msedgewebview2.exe --type=gpu-process --user-data-dir=C:\app\private-profile\EBWebView'
  Need-PresentTest ((Select-TestPresent $taskRows).ownerProfileVerified) 'Runtime child profile was refused.'
  $taskRows[1].CommandLine='msedgewebview2.exe --user-data-dir=C:\app\private-profile-extra\EBWebView'
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'external child profile is refused even with a correct browser' {
  $taskRows=New-PresentFixture;$taskRows[2].CommandLine='msedgewebview2.exe --type=gpu-process --user-data-dir=C:\other';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'missing browser profile is refused' {
  $taskRows=New-PresentFixture;$taskRows[1].CommandLine='msedgewebview2.exe';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'quoted argument text cannot impersonate a real profile switch' {
  $taskRows=New-PresentFixture;$taskRows[1].CommandLine='msedgewebview2.exe "--other=hello --user-data-dir=C:\app\private-profile"';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'duplicate profile or process type switches are refused' {
  $taskRows=New-PresentFixture;$taskRows[1].CommandLine+=' --user-data-dir=C:\app\private-profile';Reject-PresentTest {Select-TestPresent $taskRows}
  $taskRows=New-PresentFixture;$taskRows[2].CommandLine+=' --type=renderer';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'unterminated quoting does not establish an owner profile' {
  $taskRows=New-PresentFixture;$taskRows[1].CommandLine='msedgewebview2.exe "--user-data-dir=C:\app\private-profile';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'GPU cannot bypass the verified browser by being a direct root child' {
  $taskRows=New-PresentFixture;$taskRows[2].ParentProcessId=10;Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'owner GPU requires a direct browser parent' {
  $taskRows=New-PresentFixture;$taskRows+=New-PresentRow 14 11 115 'C:\runtime\msedgewebview2.exe' 'msedgewebview2.exe --type=utility'
  $taskRows[2].ParentProcessId=14;Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'owner browser requires a direct host parent' {
  $taskRows=New-PresentFixture;$taskRows+=New-PresentRow 14 10 105 'C:\app\helper.exe'
  $taskRows[1].ParentProcessId=14;Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'different WebView binary paths along the selected chain are refused' {
  $taskRows=New-PresentFixture;$taskRows[2].ExecutablePath='C:\other-runtime\msedgewebview2.exe';Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'parent PID reuse during discovery is refused' {
  $taskRows=New-PresentFixture;$script:taskMutation={param($Identity,$ReadCount)if($Identity.id -eq 10 -and $ReadCount -gt 1){$Identity.startTicks++};return $Identity}
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'path change without PID or time change is refused' {
  $taskRows=New-PresentFixture;$script:taskMutation={param($Identity,$ReadCount)if($Identity.id -eq 11 -and $ReadCount -gt 1){$Identity.path='C:\other\msedgewebview2.exe'};return $Identity}
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'a selected GPU disappearing at final recheck is refused' {
  $taskRows=New-PresentFixture;$script:taskMutation={param($Identity,$ReadCount)if($Identity.id -eq 12 -and $ReadCount -gt 1){return $null};return $Identity}
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'unverified WebView descendant cannot hide target ambiguity' {
  $taskRows=New-PresentFixture;$script:taskMutation={param($Identity,$ReadCount)if($Identity.id -eq 12){return $null};return $Identity}
  Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'children older than the alleged parent cannot establish ancestry' {
  $taskRows=New-PresentFixture;$taskRows[1].StartTicks=90;Reject-PresentTest {Select-TestPresent $taskRows}
}
Test-PresentTarget 'spaces and escaped literal quotes are parsed without inventing switches' {
  $taskTokens=@(Split-PresentCommandLine '"C:\with space\msedgewebview2.exe" --other="hello \"quoted\" --type=gpu-process" "--user-data-dir=C:\with space\private-profile"')
  Need-PresentTest ((Read-PresentSwitch $taskTokens '--user-data-dir') -eq 'C:\with space\private-profile') 'Quoted profile was not parsed.'
  Need-PresentTest ($null -eq (Read-PresentSwitch $taskTokens '--type')) 'Quoted literal became a real switch.'
}
Write-Output ('RESULT '+$script:taskPassed+' passed / '+$script:taskFailed+' failed')
if($script:taskFailed -ne 0 -or $script:taskPassed -ne 25){throw 'Pure PresentMon target checks failed.'}
