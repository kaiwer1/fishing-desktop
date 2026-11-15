param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$CoreDirectory=$PSScriptRoot
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$run=Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('seven-nine-bread-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$release='95a0081c433b2f5811a09e56c68a5c33f5effc24'
$archive=Join-Path $run 'released-source.zip'
& git -C $repoRoot archive --format=zip -o $archive $release desktop/game-core diagnostics/dual-window/DualWindowGame.cs
if($LASTEXITCODE -ne 0){throw "Missing the 0.4.0 release source commit $release."}
$old=Join-Path $run 'release'
Expand-Archive -LiteralPath $archive -DestinationPath $old
# Both factory methods come from the frozen shipping host. Isolating them avoids
# requiring a live WebView, input focus or a personal player profile for this test.
$hostSource=Get-Content -LiteralPath (Join-Path $old 'diagnostics/dual-window/DualWindowGame.cs') -Encoding UTF8 -Raw
$factory=[regex]::Match($hostSource,'(?ms)^    static FloatGameState CreateGameFixture\(.*?(?=^    static long GameInstalledLineMm\()')
if(-not $factory.Success -or -not $factory.Value.Contains('static FloatEquipmentOffers CreateGameSparesCatalog(')){throw 'Released player factory unavailable.'}
$factorySource=Join-Path $run 'ReleasedFactories.cs'
@('using System;','using System.Collections.Generic;','using FishingDesktop.GameCore;','sealed partial class DualWindowContext {',$factory.Value,'}') |
    Set-Content -LiteralPath $factorySource -Encoding utf8
$test=Join-Path $PSScriptRoot 'tests/ReleasedSpeciesBaitTests.cs'
$oldExe=Join-Path $run 'WriteSeven.exe'
$oldSources=@(Get-ChildItem -LiteralPath (Join-Path $old 'desktop/game-core') -File -Filter '*.cs' | ForEach-Object {$_.FullName})
& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:ReleasedSpeciesBaitTests /define:RELEASED_SEVEN ('/out:'+$oldExe) $oldSources $factorySource $test
if($LASTEXITCODE -ne 0){throw 'The released player writer failed to compile.'}
$journal=Join-Path $run 'player.fltj'
& $oldExe create $journal
if($LASTEXITCODE -ne 0){throw 'The released writer did not create a valid player save.'}
$currentExe=Join-Path $run 'VerifyUpgrade.exe'
$currentSources=@(Get-ChildItem -LiteralPath $CoreDirectory -File -Filter '*.cs' | ForEach-Object {$_.FullName})
& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:ReleasedSpeciesBaitTests ('/out:'+$currentExe) $currentSources $test
if($LASTEXITCODE -ne 0){throw 'The combined player test failed to compile.'}
& $currentExe $journal
if($LASTEXITCODE -ne 0){throw 'The combined migration and bait test failed.'}
$backups=@(Get-ChildItem -LiteralPath $run -File -Filter 'player.before-nine-species.*.fltj')
if($backups.Count -ne 1){throw 'Expected exactly one dedicated rollback copy.'}
& $oldExe verify $backups[0].FullName
if($LASTEXITCODE -ne 0){throw 'The original player reader rejected the rollback copy.'}
