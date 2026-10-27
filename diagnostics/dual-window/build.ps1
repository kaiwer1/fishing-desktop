param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$DesktopLayer,[switch]$NativePolicy,[switch]$ThreeScene,[switch]$Game)
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manual-input-panel.mjs'),(Join-Path $PSScriptRoot '../input-window/input-session.mjs'),(Join-Path $PSScriptRoot '../input-window/manual-evidence.mjs') -Destination $taskPage
if($Game){Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../desktop/game-page/game.html'),(Join-Path $PSScriptRoot '../../desktop/game-page/page.mjs') -Destination $taskPage}
if($ThreeScene){& node (Join-Path $PSScriptRoot '../three-scene/build.mjs') $taskPage;if($LASTEXITCODE -ne 0){throw 'Lake scene bundle did not build.'}}
$taskExecutable=Join-Path $taskRun 'DualWindowHost.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/warnaserror+',
  '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
  ('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.Core.dll')),('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.WinForms.dll')),('/out:'+$taskExecutable))
if($DesktopLayer -or $NativePolicy -or $ThreeScene -or $Game){$taskArgs+=('/win32manifest:'+(Join-Path $PSScriptRoot 'desktop.manifest'))}
foreach($taskName in @('ActivationHandshake.cs','DualWindowOwner.cs','DualWindowHost.cs','DualWindowProbe.cs','DualWindowDesktop.cs','DualWindowVisual.cs','DualWindowPolicy.cs','DualWindowNativePolicy.cs','DualWindowRenderer.cs','FrozenFrameSurface.cs','DualWindowSoak.cs','DualWindowSignals.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
foreach($taskName in @('DualWindowGame.cs','DualWindowGameProbe.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
foreach($taskName in @('FloatGameState.cs','FloatGameReducer.cs','FloatJournalCodec.cs','FloatGameStore.cs','FloatGameLifecycle.cs','FloatGameOwner.cs','FloatPageProjection.cs','FloatPageCommands.cs')){$taskArgs+=(Join-Path $PSScriptRoot ('../../desktop/game-core/'+$taskName))}
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/RunGate.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/NativeEnvironment.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/NativeSignalState.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/NativeSignalWindow.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/DisplayBarrier.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/DrawProgressGuard.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/DrawWatchdog.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowWatchdog.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowWatchdogProbe.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowDisplay.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowAvailability.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowStartup.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowScene.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'SceneFaultRecord.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowSceneFault.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowViewport.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../desktop-startup/DesktopStartup.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../desktop-startup/Win32ShellLayerRequest.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'DualWindowManual.cs')
$taskArgs+=(Join-Path $PSScriptRoot 'ManualInputRecord.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/RendererSuspender.cs')
$taskArgs+=(Join-Path $PSScriptRoot '../host-policy/BoundedFrameStream.cs')
foreach($taskName in @('DesktopLayer.cs','Win32DesktopApi.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../desktop-layer/$taskName")}
foreach($taskName in @('DurableCounterStore.cs','ViewAuthority.cs')){$taskArgs+=(Join-Path $PSScriptRoot "../state-owner/$taskName")}
$taskArgs+=(Join-Path $PSScriptRoot '../input-window/HostInstanceGuard.cs')
$taskCompilerOutput=@(& $taskCompiler @taskArgs)
$taskCompilerExit=$LASTEXITCODE
$taskCompilerOutput | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskCompilerExit -ne 0){$taskCompilerOutput | Out-Host;throw 'Dual-window host did not compile.'}
if($NativePolicy){
  & $taskCompiler /nologo /utf8output /target:winexe /platform:x64 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll ('/win32manifest:'+(Join-Path $PSScriptRoot 'desktop.manifest')) ('/out:'+(Join-Path $taskRun 'NativeEnvironmentFixture.exe')) (Join-Path $PSScriptRoot '../host-policy/NativeEnvironmentFixture.cs')
  if($LASTEXITCODE -ne 0){throw 'Native policy fixture did not compile.'}
}
[pscustomobject]@{outputDirectory=$taskRun;executable=$taskExecutable;compiled=$true;started=$false}
