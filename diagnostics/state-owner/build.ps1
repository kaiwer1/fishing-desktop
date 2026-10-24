param(
  [Parameter(Mandatory=$true)][string]$SdkDirectory,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$taskSdk=(Resolve-Path -LiteralPath $SdkDirectory).Path
$taskOut=(Resolve-Path -LiteralPath $OutputDirectory).Path
$taskRepo=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
if($taskOut.Equals($taskRepo,[StringComparison]::OrdinalIgnoreCase) -or $taskOut.StartsWith($taskRepo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Diagnostic output must remain outside the repository.'}
foreach($taskName in @('Core','WinForms')){Copy-Item -LiteralPath (Join-Path $taskSdk "lib/net462/Microsoft.Web.WebView2.$taskName.dll") -Destination $taskOut}
Copy-Item -LiteralPath (Join-Path $taskSdk 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $taskOut
Copy-Item -LiteralPath (Join-Path $taskSdk 'LICENSE.txt') -Destination (Join-Path $taskOut 'WEBVIEW2-LICENSE.txt')
$taskPage=Join-Path $taskOut 'page'
New-Item -ItemType Directory -Path $taskPage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'view.html'),(Join-Path $PSScriptRoot 'lease-session.mjs') -Destination $taskPage
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskArgs=@('/nologo','/target:winexe','/platform:x64','/warnaserror+','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/r:'+(Join-Path $taskOut 'Microsoft.Web.WebView2.Core.dll')),('/r:'+(Join-Path $taskOut 'Microsoft.Web.WebView2.WinForms.dll')),('/out:'+(Join-Path $taskOut 'StateOwnerProbeHost.exe')))
foreach($taskName in @('DurableCounterStore.cs','ViewAuthority.cs','StateOwnerProbeHost.cs','OwnerWebViewProbe.cs')){$taskArgs+=(Join-Path $PSScriptRoot $taskName)}
$taskArgs+=(Join-Path $PSScriptRoot '../input-window/HostInstanceGuard.cs')
& $taskCompiler @taskArgs
if($LASTEXITCODE -ne 0){throw 'Shared owner host did not compile.'}
