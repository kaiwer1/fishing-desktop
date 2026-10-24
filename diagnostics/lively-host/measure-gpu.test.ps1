# Run with PowerShell. No administrator access, running host or output directory
# is needed. Only function definitions are extracted; the sampler entry point,
# provider discovery, sampling loop and file output are never executed.
$ErrorActionPreference='Stop'
$taskSource=Join-Path $PSScriptRoot 'measure-gpu.ps1'
$taskTokens=$null;$taskParseErrors=$null
$taskAst=[Management.Automation.Language.Parser]::ParseFile($taskSource,[ref]$taskTokens,[ref]$taskParseErrors)
if($taskParseErrors.Count){throw 'The sampler source does not parse.'}
foreach($taskFunctionName in @('Assert-DirectOutputDirectory','Read-Provider','Valid-CounterNumber','Sum-Counter','Nullable-Stats','Counter-Value','Raw-Counters','Read-ScopedProcess','Get-ScopedSnapshot')){
  $taskDefinitions=@($taskAst.FindAll({param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $taskFunctionName
  },$true))
  if($taskDefinitions.Count -ne 1){throw ('Expected exactly one function: '+$taskFunctionName)}
  . ([scriptblock]::Create($taskDefinitions[0].Extent.Text))
}

# These local replacements never delegate to the real cmdlets, even for an
# unexpected class or PID. Tests exercise in-memory fixtures only.
function Get-CimInstance {
  [CmdletBinding()]param([string]$ClassName,[string]$Namespace,[string[]]$Property,[int]$OperationTimeoutSec)
  $script:fixtureProviderCalls.Add($ClassName)
  if($ClassName -eq 'Win32_Process'){
    if($script:fixtureProcessEnumerationFails){throw 'Synthetic process enumeration failure.'}
    return $script:fixtureCim
  }
  if(-not $script:fixtureProviders.ContainsKey($ClassName)){throw ('Unexpected synthetic provider: '+$ClassName)}
  $queue=$script:fixtureProviders[$ClassName]
  if($queue.Count -eq 0){throw 'Synthetic provider response queue exhausted.'}
  $response=$queue.Dequeue()
  if($response.fail){throw 'Synthetic GPU instance query failure.'}
  return $response.rows
}
function Get-CimClass {throw 'Schema discovery must never run in this fixed-data test.'}
function Get-Item {
  [CmdletBinding()]param([string]$LiteralPath)
  if(-not $script:fixtureDirectories.ContainsKey($LiteralPath)){throw 'Unexpected synthetic directory.'}
  return $script:fixtureDirectories[$LiteralPath]
}
function Get-Process {
  [CmdletBinding()]param([int]$Id)
  $entry=$script:fixtureProcesses[$Id]
  if($null -eq $entry){throw 'Synthetic process missing.'}
  $entry.Calls++
  if($entry.ExitFrom -gt 0 -and $entry.Calls -ge $entry.ExitFrom){throw 'Synthetic process exited.'}
  $value=[pscustomobject]@{
    StartTime=$entry.Start;TotalProcessorTime=[TimeSpan]::FromSeconds(1)
    PrivateMemorySize64=100;WorkingSet64=200;HasExited=$false
  }
  $value | Add-Member ScriptMethod Refresh {}
  $value | Add-Member ScriptMethod Dispose {}
  return $value
}
function Reset-Fixture {
  $script:fixtureCim=@();$script:fixtureProcesses=@{};$script:fixtureProviders=@{}
  $script:fixtureDirectories=@{}
  $script:fixtureProviderCalls=[Collections.Generic.List[string]]::new()
  $script:fixtureProcessEnumerationFails=$false
  $script:taskExecutable='C:\synthetic-fixture\Lively.exe';$script:taskRootId=100
  $script:taskRootStart=[datetime]::SpecifyKind([datetime]'2026-01-01T00:00:00',[DateTimeKind]::Utc)
  $script:taskTracked=@{100=[pscustomobject]@{id=100;startTicks=$taskRootStart.Ticks;parentId=$null}}
  $script:taskClassInfo=@{}
}
function Add-ProcessFixture([int]$Id,[int]$Parent,[int]$Offset,[string]$Name='child.exe',[int]$ExitFrom=0){
  $start=$script:taskRootStart.AddSeconds($Offset)
  $path=if($Name -eq 'Lively.exe'){$script:taskExecutable}else{'C:\synthetic-fixture\'+$Name}
  $script:fixtureCim += [pscustomobject]@{ProcessId=$Id;ParentProcessId=$Parent;CreationDate=$start;ExecutablePath=$path;Name=$Name}
  $script:fixtureProcesses[$Id]=[pscustomobject]@{Start=$start;ExitFrom=$ExitFrom;Calls=0}
}
function Set-ProviderFixture([string]$ClassName,$Responses){
  $queue=[Collections.Generic.Queue[object]]::new()
  foreach($response in $Responses){$queue.Enqueue($response)}
  $script:fixtureProviders[$ClassName]=$queue
}
function New-CounterFixture($Values){
  # Constructs a CIM-shaped value in memory; this is not a provider query.
  $instance=[Microsoft.Management.Infrastructure.CimInstance]::new('SyntheticGpuCounter')
  foreach($key in $Values.Keys){
    $value=$Values[$key]
    $type=if($value -is [string]){[Microsoft.Management.Infrastructure.CimType]::String}else{[Microsoft.Management.Infrastructure.CimType]::UInt64}
    $instance.CimInstanceProperties.Add([Microsoft.Management.Infrastructure.CimProperty]::Create($key,$value,$type,[Microsoft.Management.Infrastructure.CimFlags]::None))
  }
  return $instance
}
function Assert-True([bool]$Condition,[string]$Message='Assertion failed'){
  if(-not $Condition){throw $Message}
}
function Assert-Equal($Actual,$Expected,[string]$Message='Values differ'){
  if($null -eq $Expected){Assert-True ($null -eq $Actual) $Message;return}
  Assert-True ($null -ne $Actual -and $Actual -eq $Expected) ($Message+': actual='+[string]$Actual+' expected='+[string]$Expected)
}
function Assert-Throws([scriptblock]$Body,[string]$Pattern){
  $caught=$null
  try{& $Body}catch{$caught=$_}
  Assert-True ($null -ne $caught) 'Expected an exception'
  Assert-True ($caught.Exception.Message -match $Pattern) 'Unexpected exception'
}
$taskResults=[Collections.Generic.List[object]]::new()
function Check([string]$Name,[scriptblock]$Body){
  Reset-Fixture
  try{& $Body;$taskResults.Add([pscustomobject]@{name=$Name;passed=$true;error=$null})}
  catch{$taskResults.Add([pscustomobject]@{name=$Name;passed=$false;error=$_.Exception.Message})}
}
$taskTestClass='Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine'

Check 'Direct output directory and direct ancestors are accepted' {
  $root=[pscustomobject]@{Attributes=[IO.FileAttributes]::Directory;Parent=$null}
  $fixtureDirectories['fixture']=[pscustomobject]@{Attributes=[IO.FileAttributes]::Directory;Parent=$root}
  Assert-DirectOutputDirectory 'fixture'
}
Check 'A reparse output directory is rejected before output' {
  $fixtureDirectories['fixture']=[pscustomobject]@{Attributes=([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint);Parent=$null}
  Assert-Throws {Assert-DirectOutputDirectory 'fixture'} 'reparse-point'
}
Check 'A reparse ancestor is rejected even when the output leaf is direct' {
  $ancestor=[pscustomobject]@{Attributes=([IO.FileAttributes]::Directory -bor [IO.FileAttributes]::ReparsePoint);Parent=$null}
  $fixtureDirectories['fixture']=[pscustomobject]@{Attributes=[IO.FileAttributes]::Directory;Parent=$ancestor}
  Assert-Throws {Assert-DirectOutputDirectory 'fixture'} 'reparse-point'
}

Check 'A failed initial schema query does not disable a later successful instance query' {
  $taskClassInfo[$taskTestClass]=[pscustomobject]@{available=$false;errorType='SyntheticSchemaUnavailable';properties=@()}
  Set-ProviderFixture $taskTestClass @([pscustomobject]@{fail=$false;rows=@([pscustomobject]@{Name='synthetic-instance'})})
  $result=Read-Provider $taskTestClass
  Assert-True $result.available
  Assert-Equal $result.errorType $null
  Assert-Equal @($result.rows).Count 1
  Assert-Equal $fixtureProviderCalls.Count 1
  Assert-Equal $taskClassInfo[$taskTestClass].available $false 'Initial schema evidence must remain unchanged'
}
Check 'An instance query failure is missing data and the next sample can recover' {
  $taskClassInfo[$taskTestClass]=[pscustomobject]@{available=$true;errorType=$null}
  Set-ProviderFixture $taskTestClass @(
    [pscustomobject]@{fail=$true;rows=@()},
    [pscustomobject]@{fail=$false;rows=@([pscustomobject]@{Name='recovered'})}
  )
  $first=Read-Provider $taskTestClass;$second=Read-Provider $taskTestClass
  Assert-True (-not $first.available)
  Assert-True ($null -ne $first.errorType)
  Assert-Equal @($first.rows).Count 0
  Assert-True $second.available
  Assert-Equal $second.errorType $null
  Assert-Equal @($second.rows).Count 1
  Assert-Equal $fixtureProviderCalls.Count 2
}
Check 'Initial schema failure and repeated instance failures still allow later recovery' {
  $taskClassInfo[$taskTestClass]=[pscustomobject]@{available=$false;errorType='SyntheticSchemaUnavailable'}
  Set-ProviderFixture $taskTestClass @(
    [pscustomobject]@{fail=$true;rows=@()},
    [pscustomobject]@{fail=$true;rows=@()},
    [pscustomobject]@{fail=$false;rows=@([pscustomobject]@{Name='recovered'})}
  )
  $first=Read-Provider $taskTestClass;$second=Read-Provider $taskTestClass;$third=Read-Provider $taskTestClass
  Assert-True (-not $first.available -and -not $second.available -and $third.available)
  Assert-Equal $fixtureProviderCalls.Count 3
}
Check 'Successful empty provider data has zero instances and no invented zero usage' {
  $taskClassInfo[$taskTestClass]=[pscustomobject]@{available=$false;errorType='SyntheticSchemaUnavailable'}
  Set-ProviderFixture $taskTestClass @([pscustomobject]@{fail=$false;rows=@()})
  $result=Read-Provider $taskTestClass
  Assert-True $result.available
  Assert-Equal @($result.rows).Count 0
  Assert-Equal (Sum-Counter $result.rows 'utilizationPercent') $null
}
Check 'Provider queries do not require an initial schema entry' {
  Set-ProviderFixture $taskTestClass @([pscustomobject]@{fail=$false;rows=@()})
  Assert-True (Read-Provider $taskTestClass).available
  Assert-Equal $fixtureProviderCalls.Count 1
}
Check 'All missing summary values remain null' {
  $result=Nullable-Stats @($null,$null)
  Assert-Equal $result.availableSamples 0;Assert-Equal $result.mean $null;Assert-Equal $result.peak $null
}
Check 'Real zero remains a valid summary sample' {
  $result=Nullable-Stats @($null,0,10)
  Assert-Equal $result.availableSamples 2;Assert-Equal $result.mean 5;Assert-Equal $result.peak 10
}
Check 'One missing row prevents a misleading partial or zero sum' {
  Assert-Equal (Sum-Counter @([pscustomobject]@{v=0},[pscustomobject]@{v=$null}) 'v') $null
  Assert-Equal (Sum-Counter @([pscustomobject]@{v=10},[pscustomobject]@{v=$null}) 'v') $null
}
Check 'Zero counter values and absent instances are distinct' {
  Assert-Equal (Sum-Counter @([pscustomobject]@{v=0}) 'v') 0
  Assert-Equal (Sum-Counter @() 'v') $null
  Assert-Equal @(@() | Select-Object -ExpandProperty processId -Unique).Count 0
}
Check 'CIM zero, null field and missing field are distinct from unavailable rows' {
  $counter=New-CounterFixture @{Usage=[uint64]0;Missing=$null}
  Assert-Equal (Counter-Value $counter 'Usage') 0
  Assert-Equal (Counter-Value $counter 'Missing') $null
  Assert-Equal (Counter-Value $counter 'NoSuchField') $null
}
Check 'Raw counter output keeps numeric zero without publishing descriptions' {
  $counter=New-CounterFixture @{Name='synthetic';Caption='omit';Description='omit';Usage=[uint64]0}
  $result=Raw-Counters $counter
  Assert-Equal $result.Count 1;Assert-Equal $result.Usage 0
}
Check 'Invalid numeric fields cannot count as usable coverage' {
  foreach($value in @($null,-1,[double]::NaN,[double]::PositiveInfinity,'0',$false)){
    Assert-True (-not (Valid-CounterNumber $value))
    Assert-Equal (Sum-Counter @([pscustomobject]@{v=$value}) 'v') $null
  }
  Assert-True (Valid-CounterNumber ([uint64]0));Assert-True (Valid-CounterNumber 0.5)
}
Check 'Instance presence and valid numeric instance coverage differ' {
  $rows=@([pscustomobject]@{processId=100;v=$null},[pscustomobject]@{processId=200;v=0})
  Assert-Equal @($rows | Select-Object -ExpandProperty processId -Unique).Count 2
  Assert-Equal @($rows | Where-Object {Valid-CounterNumber $_.v}).Count 1
  Assert-Equal (Sum-Counter $rows 'v') $null
}
Check 'Scope accepts descendants and excludes unrelated processes and DWM' {
  Add-ProcessFixture 100 1 0 'Lively.exe';Add-ProcessFixture 200 100 1;Add-ProcessFixture 300 200 2
  Add-ProcessFixture 400 777 3;Add-ProcessFixture 500 100 4 'dwm.exe'
  $result=Get-ScopedSnapshot
  Assert-True $result.rootAlive
  Assert-Equal ($result.rows.id -join ',') '100,200,300'
  Assert-True $result.rows[0].isRoot
  Assert-Equal $result.rows[0].processName 'Lively.exe'
  Assert-True (-not $result.rows[1].isRoot)
}
Check 'Reused root PID stops the snapshot' {
  Add-ProcessFixture 100 1 10 'Lively.exe';Add-ProcessFixture 200 100 11
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive);Assert-Equal @($result.rows).Count 0
}
Check 'Root disappearance before scope acceptance stops the snapshot' {
  Add-ProcessFixture 100 1 0 'Lively.exe' 2;Add-ProcessFixture 200 100 1
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive);Assert-Equal $result.reason 'root-exited-during-scope-read'
}
Check 'Root exit during child parent recheck stops the snapshot' {
  Add-ProcessFixture 100 1 0 'Lively.exe' 3;Add-ProcessFixture 200 100 1
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive)
  Assert-Equal $result.reason 'root-exited-or-identity-unavailable-during-parent-recheck'
  Assert-Equal @($result.rows).Count 0
}
Check 'Final root identity recheck catches an exit without any children' {
  Add-ProcessFixture 100 1 0 'Lively.exe' 3
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive)
  Assert-Equal $result.reason 'root-exited-or-identity-unavailable-before-snapshot-return'
}
Check 'An unrelated process reusing a known child PID is excluded' {
  Add-ProcessFixture 100 1 0 'Lively.exe';Add-ProcessFixture 200 777 20
  $taskTracked[200]=[pscustomobject]@{id=200;startTicks=$taskRootStart.AddSeconds(1).Ticks;parentId=100}
  Assert-Equal ((Get-ScopedSnapshot).rows.id -join ',') '100'
}
Check 'A new true descendant reusing an old PID receives its new identity' {
  Add-ProcessFixture 100 1 0 'Lively.exe';Add-ProcessFixture 200 100 20
  $taskTracked[200]=[pscustomobject]@{id=200;startTicks=$taskRootStart.AddSeconds(1).Ticks;parentId=100}
  $result=Get-ScopedSnapshot
  Assert-Equal ($result.rows.id -join ',') '100,200'
  Assert-Equal $result.rows[1].identity ('200|'+$taskRootStart.AddSeconds(20).Ticks)
}
Check 'A child older than the current root generation is excluded' {
  Add-ProcessFixture 100 1 0 'Lively.exe';Add-ProcessFixture 200 100 -10
  Assert-Equal ((Get-ScopedSnapshot).rows.id -join ',') '100'
}
Check 'Previously proven orphan descendants retain scope and can have children' {
  Add-ProcessFixture 100 1 0 'Lively.exe';Add-ProcessFixture 300 200 2;Add-ProcessFixture 400 300 3
  $taskTracked[300]=[pscustomobject]@{id=300;startTicks=$taskRootStart.AddSeconds(2).Ticks;parentId=200}
  Assert-Equal ((Get-ScopedSnapshot).rows.id -join ',') '100,300,400'
}
Check 'Enumeration failure returns no valid process scope' {
  $script:fixtureProcessEnumerationFails=$true
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive)
  Assert-Equal $result.reason 'process-enumeration-unavailable';Assert-Equal @($result.rows).Count 0
}
Check 'A mismatched exact root path cannot establish scope' {
  Add-ProcessFixture 100 1 0 'Lively.exe'
  $fixtureCim[0].ExecutablePath='C:\another-fixture\Lively.exe'
  $result=Get-ScopedSnapshot
  Assert-True (-not $result.rootAlive);Assert-Equal @($result.rows).Count 0
}

$taskFailures=@($taskResults | Where-Object {-not $_.passed})
[pscustomobject]@{
  passed=($taskFailures.Count -eq 0);tests=$taskResults.Count;failed=$taskFailures.Count
  liveProvidersUsed=$false;sourceEntryPointExecuted=$false;results=$taskResults
} | ConvertTo-Json -Depth 5
if($taskFailures.Count){throw ('Fixed-data tests failed: '+($taskFailures.name -join '; '))}
