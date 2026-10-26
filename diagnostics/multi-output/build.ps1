param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskSdk=(Resolve-Path -LiteralPath $SdkDirectory).Path
$taskRun=Join-Path $taskRoot ('multi-output-build-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
foreach($taskName in @('Core','WinForms')){Copy-Item -LiteralPath (Join-Path $taskSdk "lib/net462/Microsoft.Web.WebView2.$taskName.dll") -Destination $taskRun}
Copy-Item -LiteralPath (Join-Path $taskSdk 'runtimes/win-x64/native/WebView2Loader.dll') -Destination $taskRun
Copy-Item -LiteralPath (Join-Path $taskSdk 'LICENSE.txt') -Destination (Join-Path $taskRun 'WEBVIEW2-LICENSE.txt')
$taskPage=Join-Path $taskRun 'page';New-Item -ItemType Directory -Path $taskPage | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'page.html') -Destination $taskPage
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'MultiOutputProbe.exe'
$taskArgs=@('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/warnaserror+',
    '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',
    ('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.Core.dll')),('/r:'+(Join-Path $taskRun 'Microsoft.Web.WebView2.WinForms.dll')),
    ('/win32manifest:'+(Join-Path $PSScriptRoot '../dual-window/desktop.manifest')),('/out:'+$taskExecutable),
    (Join-Path $PSScriptRoot 'MultiOutputProbe.cs'),(Join-Path $PSScriptRoot 'OutputPolicy.cs'),(Join-Path $PSScriptRoot '../dual-window/DualWindowOwner.cs'),
    (Join-Path $PSScriptRoot '../state-owner/DurableCounterStore.cs'),(Join-Path $PSScriptRoot '../state-owner/ViewAuthority.cs'),
    (Join-Path $PSScriptRoot '../host-policy/RunGate.cs'),(Join-Path $PSScriptRoot '../input-window/HostInstanceGuard.cs'))
$taskCompile=@(& $taskCompiler @taskArgs);$taskExit=$LASTEXITCODE
$taskCompile | Set-Content -LiteralPath (Join-Path $taskRun 'build-output.txt') -Encoding utf8
if($taskExit -ne 0){$taskCompile | Out-Host;throw 'Logical multi-output probe did not compile.'}
[pscustomobject]@{compiled=$true;started=$false;outputDirectory=$taskRun;executable=$taskExecutable}
