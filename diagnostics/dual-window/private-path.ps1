# Shared validation only; importing this file creates no processes or directories.
function Get-DualWindowPrivateRoot([string]$Directory) {
  $taskRoot=(Resolve-Path -LiteralPath $Directory -ErrorAction Stop).Path
  if(-not (Test-Path -LiteralPath $taskRoot -PathType Container)){throw 'OutputDirectory must be an existing directory.'}
  $taskRepo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
  if($taskRoot.Equals($taskRepo,[StringComparison]::OrdinalIgnoreCase) -or $taskRoot.StartsWith($taskRepo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
    throw 'Diagnostic output must remain outside the repository.'
  }
  $taskAncestor=Get-Item -LiteralPath $taskRoot
  while($null -ne $taskAncestor){
    if(($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Use a direct output path without reparse-point ancestors.'}
    $taskAncestor=$taskAncestor.Parent
  }
  return $taskRoot
}
