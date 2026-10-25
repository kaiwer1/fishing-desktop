param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$DesktopLayer)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskSdk=(Resolve-Path -LiteralPath $SdkDirectory).Path
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if(-not (Test-Path -LiteralPath $taskCompiler -PathType Leaf)){throw 'The .NET Framework x64 compiler is unavailable.'}
$taskRun=Join-Path $taskRoot ('dual-window-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
foreach($taskName in @('Core','WinForms')){Copy-Item -LiteralPath (Join-Path $taskSdk "lib/net462/Microsoft.Web.WebView2.$taskName.dll") -Destination $taskRun}
Copy-Item -LiteralPath (Join-Path $taskSdk 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $taskRun
Copy-Item -LiteralPath (Join-Path $taskSdk 'LICENSE.txt') -Destination (Join-Path $taskRun 'WEBVIEW2-LICENSE.txt')
$taskPage=Join-Path $taskRun 'page'
New-Item -ItemType Directory -Path $taskPage | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'view.html'),(Join-Path $PSScriptRoot 'visual-probe.mjs'),(Join-Path $PSScriptRoot '../state-owner/lease-session.mjs') -Destination $taskPage
$taskExecutable=Join-Path $taskRun 'DualWindowHost.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/warnaserror+',
  '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
  ('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.Core.dll')),('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.WinForms.dll')),('/out:'+$taskExecutable))
if($DesktopLayer){$taskArgs+=('/win32manifest:'+(Join-Path $PSScriptRoot 'desktop.manifest'))}
foreach($taskName in @('ActivationHandshake.cs','DualWindowOwner.cs','DualWindowHost.cs','DualWindowProbe.cs','DualWindowDesktop.cs','DualWindowVisual.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
foreach($taskName in @('DesktopLayer.cs','Win32DesktopApi.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../desktop-layer/$taskName")}
foreach($taskName in @('DurableCounterStore.cs','ViewAuthority.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../state-owner/$taskName")}
$taskArgs+=(Join-Path $PSScriptRoot '../input-window/HostInstanceGuard.cs')
$taskCompilerOutput=@(& $taskCompiler @taskArgs)
$taskCompilerExit=$LASTEXITCODE
$taskCompilerOutput | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskCompilerExit -ne 0){$taskCompilerOutput | Out-Host;throw 'Dual-window host did not compile.'}
[pscustomobject]@{outputDirectory=$taskRun;executable=$taskExecutable;compiled=$true;started=$false}
