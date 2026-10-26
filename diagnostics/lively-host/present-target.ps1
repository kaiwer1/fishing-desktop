# Pure scope selection. Only the caller supplies process rows and live identity
# observations; this helper never enumerates, launches, stops or traces anything.
function Split-PresentCommandLine([string]$CommandLine){
  if($null -eq $CommandLine -or $CommandLine.Length -gt 32768){throw 'Scoped process command line is unavailable or exceeds its bound.'}
  $taskTokens=[Collections.Generic.List[string]]::new();$taskIndex=0
  while($taskIndex -lt $CommandLine.Length){
    while($taskIndex -lt $CommandLine.Length -and [char]::IsWhiteSpace($CommandLine[$taskIndex])){$taskIndex++}
    if($taskIndex -ge $CommandLine.Length){break}
    $taskValue=[Text.StringBuilder]::new();$taskQuoted=$false
    while($taskIndex -lt $CommandLine.Length){
      $taskCharacter=$CommandLine[$taskIndex]
      if(-not $taskQuoted -and [char]::IsWhiteSpace($taskCharacter)){break}
      if($taskCharacter -eq '\'){
        $taskSlashStart=$taskIndex
        while($taskIndex -lt $CommandLine.Length -and $CommandLine[$taskIndex] -eq '\'){$taskIndex++}
        $taskSlashes=$taskIndex-$taskSlashStart
        if($taskIndex -lt $CommandLine.Length -and $CommandLine[$taskIndex] -eq '"'){
          [void]$taskValue.Append(('\'*[int][Math]::Floor($taskSlashes/2)))
          if($taskSlashes%2 -eq 1){[void]$taskValue.Append('"');$taskIndex++;continue}
          # Even slashes leave this quote for the normal quote branch.
        }else{[void]$taskValue.Append(('\'*$taskSlashes));continue}
      }
      if($CommandLine[$taskIndex] -eq '"'){
        if($taskQuoted -and $taskIndex+1 -lt $CommandLine.Length -and $CommandLine[$taskIndex+1] -eq '"'){
          [void]$taskValue.Append('"');$taskIndex+=2
        }else{$taskQuoted=-not $taskQuoted;$taskIndex++}
      }else{[void]$taskValue.Append($CommandLine[$taskIndex]);$taskIndex++}
    }
    if($taskQuoted){throw 'Unterminated process command-line quote.'}
    $taskTokens.Add($taskValue.ToString())
  }
  return $taskTokens.ToArray()
}
function Read-PresentSwitch([string[]]$Tokens,[string]$Name){
  $taskFound=[Collections.Generic.List[string]]::new()
  foreach($taskToken in $Tokens){
    if($taskToken.Equals($Name,[StringComparison]::OrdinalIgnoreCase)){throw 'A scoped process switch has no value.'}
    if($taskToken.StartsWith($Name+'=',[StringComparison]::OrdinalIgnoreCase)){$taskFound.Add($taskToken.Substring($Name.Length+1))}
  }
  if($taskFound.Count -gt 1){throw 'Duplicate scoped process switch.'}
  if($taskFound.Count -eq 1){if(-not $taskFound[0]){throw 'An empty scoped process switch is invalid.'};return $taskFound[0]}
  return $null
}
function Same-PresentIdentity($Left,$Right){
  return $null -ne $Left -and $null -ne $Right -and $Left.id -eq $Right.id -and $Left.startTicks -eq $Right.startTicks -and
    $Left.path -and $Right.path -and $Left.path.Equals($Right.path,[StringComparison]::OrdinalIgnoreCase)
}
function Copy-PresentIdentity($Identity){
  if($null -eq $Identity){return $null}
  return [pscustomobject]@{id=$Identity.id;parentId=$Identity.parentId;startTicks=$Identity.startTicks;
    startTimeUtc=$Identity.startTimeUtc;path=$Identity.path;name=$Identity.name;playerAncestor=$null;gpuFlag=$false}
}
function Test-PresentOwnerProfile([string]$Actual,[string]$Expected){
  return $Actual -and $Expected -and ($Actual.Equals($Expected,[StringComparison]::OrdinalIgnoreCase) -or
    $Actual.Equals((Join-Path $Expected 'EBWebView'),[StringComparison]::OrdinalIgnoreCase))
}
function Get-PresentCaptureTarget {
  param([Parameter(Mandatory=$true)][object[]]$Rows,[Parameter(Mandatory=$true)][string]$RootExecutable,
    [Parameter(Mandatory=$true)][ValidateSet('Lively','Owner')][string]$Kind,
    [string]$PlayerExecutable,[string]$OwnerProfileDirectory,[Parameter(Mandatory=$true)][scriptblock]$ReadIdentity)
  $taskRoots=@($Rows | Where-Object {$_.ExecutablePath -and $_.ExecutablePath.Equals($RootExecutable,[StringComparison]::OrdinalIgnoreCase)})
  if($taskRoots.Count -ne 1){throw 'Expected exactly one running instance of the requested host.'}
  $taskRoot=& $ReadIdentity $taskRoots[0]
  if($null -eq $taskRoot){throw 'Root identity could not be verified.'}
  $taskById=@{};$taskByParent=@{}
  foreach($taskRow in $Rows){
    $taskRowId=[int]$taskRow.ProcessId
    if($taskById.ContainsKey($taskRowId)){throw 'Process enumeration has duplicate identities.'}
    $taskById[$taskRowId]=$taskRow;$taskParentId=[int]$taskRow.ParentProcessId
    if(-not $taskByParent.ContainsKey($taskParentId)){$taskByParent[$taskParentId]=[Collections.Generic.List[object]]::new()}
    $taskByParent[$taskParentId].Add($taskRow)
  }
  $taskAccepted=@{};$taskAccepted[$taskRoot.id]=$taskRoot
  $taskQueue=[Collections.Generic.Queue[object]]::new();$taskQueue.Enqueue($taskRoot)
  while($taskQueue.Count -gt 0){
    $taskParent=$taskQueue.Dequeue()
    foreach($taskChildRow in @($taskByParent[$taskParent.id])){
      if($null -eq $taskChildRow -or $taskAccepted.ContainsKey([int]$taskChildRow.ProcessId)){continue}
      $taskChild=& $ReadIdentity $taskChildRow
      if($null -eq $taskChild -or $taskChild.startTicks -lt $taskParent.startTicks){
        if($Kind -eq 'Owner' -and $taskChildRow.Name -ieq 'msedgewebview2.exe'){throw 'A WebView descendant could not be verified.'}
        continue
      }
      if(-not (Same-PresentIdentity $taskParent (& $ReadIdentity $taskById[$taskParent.id]))){throw 'A parent changed during target discovery.'}
      $taskAccepted[$taskChild.id]=$taskChild;$taskQueue.Enqueue($taskChild)
    }
  }
  $taskPlayer=$null;$taskBrowser=$null
  if($Kind -eq 'Lively'){
    $taskPlayers=@($taskAccepted.Values | Where-Object {$_.path.Equals($PlayerExecutable,[StringComparison]::OrdinalIgnoreCase)})
    if($taskPlayers.Count -ne 1){throw 'Expected one proven wallpaper player; broad capture is refused.'}
    $taskPlayer=$taskPlayers[0]
  }elseif(-not $OwnerProfileDirectory){throw 'Owner capture requires its exact private profile.'}
  $taskTargets=[Collections.Generic.List[object]]::new();$taskBrowsers=[Collections.Generic.List[object]]::new()
  foreach($taskProcess in @($taskAccepted.Values | Where-Object {$_.name -ieq 'msedgewebview2.exe'})){
    $taskTokens=@(Split-PresentCommandLine $taskProcess.command)
    $taskType=Read-PresentSwitch $taskTokens '--type'
    if($Kind -eq 'Owner'){
      $taskProfile=Read-PresentSwitch $taskTokens '--user-data-dir'
      if($null -ne $taskProfile -and -not (Test-PresentOwnerProfile $taskProfile $OwnerProfileDirectory)){throw 'An owner WebView uses another profile.'}
      if($null -eq $taskType){
        if($null -eq $taskProfile){throw 'The owner browser profile is unavailable.'}
        $taskBrowsers.Add($taskProcess)
      }
    }
    if($taskType -ne 'gpu-process'){continue}
    $taskAncestor=$taskProcess;$taskSeen=@{};$taskUnderPlayer=$false
    while($taskAncestor.id -ne $taskRoot.id){
      if($taskSeen.ContainsKey($taskAncestor.id) -or -not $taskAccepted.ContainsKey($taskAncestor.parentId)){throw 'Unproven capture ancestry.'}
      $taskSeen[$taskAncestor.id]=$true;$taskAncestor=$taskAccepted[$taskAncestor.parentId]
      if($Kind -eq 'Lively' -and $taskAncestor.id -eq $taskPlayer.id){$taskUnderPlayer=$true}
    }
    if($Kind -eq 'Owner' -or $taskUnderPlayer){$taskTargets.Add($taskProcess)}
  }
  if($taskTargets.Count -ne 1){throw 'Expected one proven GPU process; zero or ambiguous targets are refused.'}
  $taskGpu=$taskTargets[0]
  if($Kind -eq 'Owner'){
    if($taskBrowsers.Count -ne 1){throw 'Expected one owner browser with the exact private profile.'}
    $taskBrowser=$taskBrowsers[0];$taskAncestor=$taskGpu;$taskUnderBrowser=$false
    while($taskAncestor.id -ne $taskRoot.id){
      if($taskAncestor.id -eq $taskBrowser.id){$taskUnderBrowser=$true}
      if($taskAncestor.name -ieq 'msedgewebview2.exe' -and -not $taskAncestor.path.Equals($taskGpu.path,[StringComparison]::OrdinalIgnoreCase)){throw 'WebView executable paths changed within the GPU chain.'}
      $taskAncestor=$taskAccepted[$taskAncestor.parentId]
    }
    if(-not $taskUnderBrowser){throw 'GPU process is not below the verified owner browser.'}
    if($taskBrowser.parentId -ne $taskRoot.id -or $taskGpu.parentId -ne $taskBrowser.id){throw 'Owner capture requires a direct root browser GPU chain.'}
  }
  # Recheck every accepted ancestor on the selected chain, including the root,
  # after the entire discovery. No stale parent can establish provenance.
  $taskAncestor=$taskGpu
  for(;;){
    if(-not (Same-PresentIdentity $taskAncestor (& $ReadIdentity $taskById[$taskAncestor.id]))){throw 'A selected identity changed before discovery completed.'}
    if($taskAncestor.id -eq $taskRoot.id){break};$taskAncestor=$taskAccepted[$taskAncestor.parentId]
  }
  $taskRootCopy=Copy-PresentIdentity $taskRoot;$taskPlayerCopy=Copy-PresentIdentity $taskPlayer
  $taskBrowserCopy=Copy-PresentIdentity $taskBrowser;$taskGpuCopy=Copy-PresentIdentity $taskGpu
  $taskGpuCopy.gpuFlag=$true
  if($Kind -eq 'Lively'){$taskPlayerCopy.playerAncestor=$taskPlayer.id;$taskGpuCopy.playerAncestor=$taskPlayer.id}
  return [pscustomobject]@{root=$taskRootCopy;player=$taskPlayerCopy;browser=$taskBrowserCopy;gpu=$taskGpuCopy;
    scopeKind=$Kind;ownerProfileVerified=($Kind -eq 'Owner')}
}
