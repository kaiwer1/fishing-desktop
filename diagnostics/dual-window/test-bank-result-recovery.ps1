param([Parameter(Mandatory=$true)][string]$SdkDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'private-path.ps1')
$private=Get-DualWindowPrivateRoot $OutputDirectory
$run=Join-Path $private ('bank-result-recovery-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $run 'BankResultRecoveryTests.exe'
$argsForCompiler=@('/nologo','/utf8output','/codepage:65001','/target:exe','/platform:x64','/warnaserror+','/main:BankResultRecoveryTests',
  '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',('/out:'+$exe))
foreach($kind in 'Core','WinForms'){
    $dll="Microsoft.Web.WebView2.$kind.dll"
    Copy-Item -LiteralPath (Join-Path $SdkDirectory "lib/net462/$dll") -Destination $run
    $argsForCompiler+=('/r:'+(Join-Path $run $dll))
}
# Compile the production partial class with a separate headless test entry.
# No copy of the test constructor or its fixture is added to the player.
foreach($dir in $PSScriptRoot,(Join-Path $PSScriptRoot '../../desktop/game-core'),(Join-Path $PSScriptRoot '../host-policy')){
    $argsForCompiler+=@(Get-ChildItem -LiteralPath $dir -Filter '*.cs' -File | Where-Object {$_.Name -notmatch '(Tests|Fixture|Tool)\.cs$'} | ForEach-Object FullName)
}
foreach($file in '../desktop-startup/DesktopStartup.cs','../desktop-startup/Win32ShellLayerRequest.cs','../desktop-layer/DesktopLayer.cs',
    '../desktop-layer/Win32DesktopApi.cs','../state-owner/DurableCounterStore.cs','../state-owner/ViewAuthority.cs','../input-window/HostInstanceGuard.cs',
    'tests/BankResultRecoveryTests.cs'){
    $argsForCompiler+=(Join-Path $PSScriptRoot $file)
}
& $compiler @argsForCompiler
if($LASTEXITCODE -ne 0){throw 'Bank result recovery tests did not compile.'}
$lines=@(& $exe $run)
if($LASTEXITCODE -ne 0){throw 'Bank result recovery failed.'}
$report=[pscustomobject]@{passed=$true;checks=$lines;windowOpened=$false;personalSaveRead=$false;physicsSimulated=$false;outputDirectory=$run}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8
$report | ConvertTo-Json -Depth 4
