param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('output-topology-native-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'OutputTopologyProbe.exe'
$taskArgs=@('/nologo','/utf8output','/target:winexe','/platform:x64','/warnaserror+','/r:System.Web.Extensions.dll',
  ('/win32manifest:'+(Join-Path $PSScriptRoot 'desktop.manifest')),('/out:'+$taskExecutable))
foreach($taskName in @('OutputTopology.cs','Win32TopologyQuery.cs','OutputTopologyProbe.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Read-only topology native probe did not compile.'}
[pscustomobject]@{compiled=$true;started=$false;outputDirectory=$taskRun;executable=$taskExecutable}
