param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('output-environment-native-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'OutputEnvironmentProbe.exe'
$taskArgs=@('/nologo','/utf8output','/target:winexe','/platform:x64','/warnaserror+','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
  ('/win32manifest:'+(Join-Path $PSScriptRoot '../output-topology/desktop.manifest')),('/out:'+$taskExecutable))
foreach($taskName in @('OutputEnvironmentCapture.cs','CurrentOutputEnvironment.cs','OutputEnvironmentProbe.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
foreach($taskName in @('NativeEnvironment.cs','RunGate.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../host-policy/$taskName")}
foreach($taskName in @('OutputTopology.cs','Win32TopologyQuery.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../output-topology/$taskName")}
$taskBuild=@(& $taskCompiler @taskArgs);$taskBuildExit=$LASTEXITCODE
$taskBuild|Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskBuildExit -ne 0){$taskBuild|Out-Host;throw 'Per-output environment native probe did not compile.'}
[pscustomobject]@{compiled=$true;started=$false;outputDirectory=$taskRun;executable=$taskExecutable}
