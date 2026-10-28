param(
  [Parameter(Mandatory=$true)][string]$SourcePath,
  [Parameter(Mandatory=$true)][string]$BackupPath,
  [Parameter(Mandatory=$true)][string]$CandidatePath,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskSource=[IO.Path]::GetFullPath($SourcePath)
$taskBackup=[IO.Path]::GetFullPath($BackupPath)
$taskCandidate=[IO.Path]::GetFullPath($CandidatePath)
$taskRun=Join-Path $taskRoot ('profile-upgrade-tool-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'FloatProfileUpgradeTool.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/warnaserror+','/r:System.Web.Extensions.dll',('/out:'+$taskExecutable))
foreach($taskName in @('FloatEquipmentLedger.cs','FloatEquipmentCodec.cs','FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatJournalArchive.cs','FloatProfileUpgrade.cs','FloatProfileUpgradeTool.cs')){
  $taskArgs+=(Join-Path $PSScriptRoot $taskName)
}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Profile candidate tool did not compile.'}
& $taskExecutable $taskSource $taskBackup $taskCandidate
if($LASTEXITCODE -ne 0){throw 'Candidate preparation was not confirmed. Retain created files for inspection; no profile was activated.'}
