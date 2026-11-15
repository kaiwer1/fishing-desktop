param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$run=Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('bread-bait-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$exe=Join-Path $run 'BreadBaitTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | ForEach-Object {$_.FullName})
$sources+=@(Join-Path $PSScriptRoot 'tests/BottomOwnedKitTests.cs')
$sources+=@(Join-Path $PSScriptRoot 'tests/BreadBaitTests.cs')
& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:BreadBaitTests ('/out:'+$exe) $sources
if($LASTEXITCODE -ne 0){throw 'Bread bait tests did not compile.'}
& $exe $run
if($LASTEXITCODE -ne 0){throw 'Bread bait tests failed.'}
