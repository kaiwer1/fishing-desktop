param(
  [Parameter(Mandatory=$true)][string]$SdkDirectory,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskSdk=(Resolve-Path -LiteralPath $SdkDirectory).Path
$taskOutput=(Resolve-Path -LiteralPath $OutputDirectory).Path
foreach($taskName in @('Core','WinForms')) {
  Copy-Item -LiteralPath (Join-Path $taskSdk "lib/net462/Microsoft.Web.WebView2.$taskName.dll") -Destination $taskOutput
}
Copy-Item -LiteralPath (Join-Path $taskSdk 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $taskOutput
Copy-Item -LiteralPath (Join-Path $taskSdk 'LICENSE.txt') -Destination (Join-Path $taskOutput 'WEBVIEW2-LICENSE.txt')
$taskPage=Join-Path $taskOutput 'page'
New-Item -ItemType Directory -Path $taskPage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'index.html'),(Join-Path $PSScriptRoot 'input-session.mjs'),(Join-Path $PSScriptRoot 'page-bridge.mjs') -Destination $taskPage
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'page-activation.mjs') -Destination $taskPage
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll',('/reference:'+(Join-Path $taskOutput 'Microsoft.Web.WebView2.Core.dll')),('/reference:'+(Join-Path $taskOutput 'Microsoft.Web.WebView2.WinForms.dll')),('/out:'+(Join-Path $taskOutput 'InputProbeHost.exe')),(Join-Path $PSScriptRoot 'InputProbeHost.cs'),(Join-Path $PSScriptRoot 'HostInstanceGuard.cs'),(Join-Path $PSScriptRoot 'InputHostLifecycle.cs'),(Join-Path $PSScriptRoot 'LifecycleProbe.cs'),(Join-Path $PSScriptRoot 'ManualInputProbe.cs'))
$taskArgs+=(Join-Path $PSScriptRoot '../dual-window/ActivationHandshake.cs')
& $taskCompiler @taskArgs
if($LASTEXITCODE -ne 0){throw 'Input window compilation failed'}
Write-Output (Join-Path $taskOutput 'InputProbeHost.exe')
