param(
  [Parameter(Mandatory=$true)][ValidateSet('inspect','backup')][string]$Action,
  [Parameter(Mandatory=$true)][string]$SourcePath,
  [string]$DestinationPath,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
if($Action -eq 'backup' -and [string]::IsNullOrWhiteSpace($DestinationPath)){throw 'Backup requires an explicit new destination path.'}
if($Action -eq 'inspect' -and -not [string]::IsNullOrWhiteSpace($DestinationPath)){throw 'Inspect does not accept a destination.'}
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskSource=[IO.Path]::GetFullPath($SourcePath)
$taskDestination=if($Action -eq 'backup'){[IO.Path]::GetFullPath($DestinationPath)}else{$null}
$taskRun=Join-Path $taskRoot ('journal-tool-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatJournalTool.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/warnaserror+','/r:System.Web.Extensions.dll',('/out:'+$taskExecutable))
foreach($taskName in @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatJournalArchive.cs','FloatJournalTool.cs')){
  $taskArgs+=(Join-Path $PSScriptRoot $taskName)
}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Journal maintenance tool did not compile.'}
if($Action -eq 'backup'){& $taskExecutable $Action $taskSource $taskDestination}else{& $taskExecutable $Action $taskSource}
if($LASTEXITCODE -ne 0){throw 'Journal operation was not confirmed. Source was not changed; keep any unconfirmed destination for inspection.'}
