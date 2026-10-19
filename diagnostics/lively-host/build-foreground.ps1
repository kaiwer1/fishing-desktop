param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'The .NET Framework C# compiler is unavailable.' }
$taskParent = (Resolve-Path -LiteralPath (Split-Path -Parent $OutputPath)).Path
$taskOutput = Join-Path $taskParent (Split-Path -Leaf $OutputPath)
$taskSource = Join-Path $PSScriptRoot 'ForegroundProbe.cs'
& $taskCompiler /nologo /utf8output /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ('/out:' + $taskOutput) $taskSource
if ($LASTEXITCODE -ne 0) { throw 'Foreground probe compilation failed.' }
Write-Output $taskOutput
