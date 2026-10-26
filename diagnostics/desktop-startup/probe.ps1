param([Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$PrepareMissingLayer)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../dual-window/private-path.ps1')
$taskRoot=Get-DualWindowPrivateRoot $OutputDirectory
$taskRun=Join-Path $taskRoot ('desktop-startup-probe-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskRun | Out-Null
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$taskExecutable=Join-Path $taskRun 'NativeStartupProbe.exe'
$taskSources=@('DesktopStartup.cs','Win32ShellLayerRequest.cs','NativeStartupProbe.cs','../desktop-layer/DesktopLayer.cs','../desktop-layer/Win32DesktopApi.cs','../host-policy/NativeEnvironment.cs','../host-policy/RunGate.cs','../input-window/HostInstanceGuard.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $taskCompiler /nologo /utf8output /target:exe /platform:x64 /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ('/win32manifest:'+(Join-Path $PSScriptRoot '../dual-window/desktop.manifest')) ('/out:'+$taskExecutable) @taskSources
if($LASTEXITCODE -ne 0){throw 'Native startup probe did not compile.'}
$taskArguments=if($PrepareMissingLayer){'--prepare-missing-layer'}else{''}
$taskStartArgs=@{FilePath=$taskExecutable;WorkingDirectory=$taskRun;WindowStyle='Hidden';PassThru=$true}
if($taskArguments){$taskStartArgs.ArgumentList=$taskArguments}
$taskProcess=Start-Process @taskStartArgs
$taskStart=$taskProcess.StartTime.ToUniversalTime().Ticks
$taskForced=$false;$taskExit=$null
try{if(-not $taskProcess.WaitForExit(10000)){throw 'Native startup probe exceeded its outer deadline.'};$taskExit=$taskProcess.ExitCode}
finally{
    if(-not $taskProcess.HasExited){
        if($taskProcess.StartTime.ToUniversalTime().Ticks -ne $taskStart -or -not $taskProcess.Path.Equals($taskExecutable,[StringComparison]::OrdinalIgnoreCase)){throw 'Startup probe identity changed; refusing cleanup.'}
        $taskProcess.Kill();$taskForced=$true;if(-not $taskProcess.WaitForExit(5000)){throw 'Own startup probe did not close.'}
    }
    $taskProcess.Dispose()
    [pscustomobject]@{exitCode=$taskExit;forcedCleanup=$taskForced;ownProcessExited=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRun 'process-summary.json') -Encoding utf8
}
if($taskForced -or $taskExit -ne 0){throw ('Native startup probe failed. Private output: '+$taskRun)}
Get-Content -LiteralPath (Join-Path $taskRun 'native-summary.json') -Raw
