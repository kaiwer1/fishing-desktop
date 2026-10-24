param(
  [Parameter(Mandatory=$true)][string]$OutputDirectory,
  [switch]$NativeHiddenWindowBoundary
)
$ErrorActionPreference='Stop'
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')).TrimEnd([IO.Path]::DirectorySeparatorChar)
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
if(-not (Test-Path -LiteralPath $taskOutput -PathType Container)){throw 'OutputDirectory must be an existing directory.'}
if($taskOutput.Equals($taskRepository,[StringComparison]::OrdinalIgnoreCase) -or $taskOutput.StartsWith($taskRepository+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'OutputDirectory must be outside the repository.'}
$taskAncestor=Get-Item -LiteralPath $taskOutput -Force
while($null -ne $taskAncestor){
  if(($taskAncestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'OutputDirectory or its ancestor is a reparse point.'}
  $taskAncestor=$taskAncestor.Parent
}
$taskRun=Join-Path $taskOutput ('desktop-layer-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path -LiteralPath $taskCompiler -PathType Leaf)){throw '.NET Framework x64 compiler unavailable.'}
$taskLibrary=Join-Path $taskRun 'DesktopLayer.dll'
$taskSources=@((Join-Path $PSScriptRoot 'DesktopLayer.cs'),(Join-Path $PSScriptRoot 'Win32DesktopApi.cs'))
& $taskCompiler /nologo /utf8output /target:library /platform:x64 ('/out:'+$taskLibrary) @taskSources
if($LASTEXITCODE -ne 0){throw 'Desktop layer library did not compile.'}
$taskExecutable=Join-Path $taskRun 'DesktopLayerTests.exe'
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/r:'+$taskLibrary) ('/out:'+$taskExecutable) (Join-Path $PSScriptRoot 'DesktopLayerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Desktop layer tests did not compile.'}
$taskArguments=@()
if($NativeHiddenWindowBoundary){$taskArguments=@('--native-hidden-boundary')}
$taskText=@(& $taskExecutable @taskArguments)
$taskExit=$LASTEXITCODE
$taskText | Set-Content -LiteralPath (Join-Path $taskRun 'test-output.txt') -Encoding utf8
$taskPassed=@($taskText | Where-Object {$_ -like 'PASS *'})
$taskFailed=@($taskText | Where-Object {$_ -like 'FAIL *'})
$taskReport=[pscustomobject]@{passed=($taskExit -eq 0 -and $taskPassed.Count -gt 0 -and $taskFailed.Count -eq 0);passedCount=$taskPassed.Count;failedCount=$taskFailed.Count;checks=$taskPassed;failures=$taskFailed;fakeDesktop=(-not $NativeHiddenWindowBoundary);ownHiddenWindowOnly=[bool]$NativeHiddenWindowBoundary;explorerMutated=$false;actualDesktopAttachment=$false;outputDirectory=$taskRun}
$taskReport | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8
$taskReport | ConvertTo-Json -Depth 5
if(-not $taskReport.passed){throw 'Desktop layer tests failed. See private summary.json.'}
