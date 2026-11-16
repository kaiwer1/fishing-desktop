param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$CoreDirectory=$PSScriptRoot
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$run=Join-Path (Get-DualWindowPrivateRoot $OutputDirectory) ('fish-upgrade-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$repoRoot=(Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$tests=@('ReleasedSpeciesBaitTests.cs','ReleasedNineSpeciesTests.cs') | ForEach-Object {Join-Path $PSScriptRoot ('tests/'+$_)}
function New-HistoricalWriter([string]$name,[string]$commit) {
    $archive=Join-Path $run ($name+'.zip')
    & git -C $repoRoot archive --format=zip -o $archive $commit desktop/game-core diagnostics/dual-window/DualWindowGame.cs
    if($LASTEXITCODE -ne 0){throw "Missing historical source $commit."}
    $old=Join-Path $run $name
    Expand-Archive -LiteralPath $archive -DestinationPath $old
    # Use the original player's factories without launching its window.
    $hostSource=Get-Content -LiteralPath (Join-Path $old 'diagnostics/dual-window/DualWindowGame.cs') -Encoding UTF8 -Raw
    $factory=[regex]::Match($hostSource,'(?ms)^    static FloatGameState CreateGameFixture\(.*?(?=^    static long GameInstalledLineMm\()')
    if(-not $factory.Success -or -not $factory.Value.Contains('static FloatEquipmentOffers CreateGameSparesCatalog(')){throw 'Historical player factory unavailable.'}
    $factorySource=Join-Path $old 'HistoricalFactories.cs'
    @('using System;','using System.Collections.Generic;','using FishingDesktop.GameCore;','sealed partial class DualWindowContext {',$factory.Value,'}') |
        Set-Content -LiteralPath $factorySource -Encoding utf8
    $exe=Join-Path $run ($name+'.exe')
    $sources=@(Get-ChildItem -LiteralPath (Join-Path $old 'desktop/game-core') -File -Filter '*.cs' | ForEach-Object {$_.FullName})
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:ReleasedSpeciesBaitTests ('/define:'+$name) ('/out:'+$exe) $sources $factorySource $tests | Out-Host
    if($LASTEXITCODE -ne 0){throw "Historical writer $name failed to compile."}
    return $exe
}
function Invoke-JournalTest([string]$exe,[string[]]$arguments){
    & $exe @arguments
    if($LASTEXITCODE -ne 0){throw "Journal test failed using $exe."}
}
$oldExe=New-HistoricalWriter RELEASED_SEVEN 95a0081c433b2f5811a09e56c68a5c33f5effc24
$nineExe=New-HistoricalWriter RELEASED_NINE 11224ce21c091d6aabf2110c750770c44834e03c
$skillExe=New-HistoricalWriter NINE_SKILL e1627e0b112d4a904dd260da13283b771ba3c798
$journal=Join-Path $run 'player.fltj'
Invoke-JournalTest $oldExe @('create',$journal)
$nineNew=Join-Path $run 'nine.fltj'
$nineUpgraded=Join-Path $run 'nine-skill.fltj'
Copy-Item -LiteralPath $journal -Destination $nineUpgraded
Invoke-JournalTest $nineExe @('create',$nineNew)
Invoke-JournalTest $nineExe @('upgrade-seven',$nineUpgraded)
Invoke-JournalTest $skillExe @('learn',$nineUpgraded)
$currentExe=Join-Path $run 'VerifyUpgrade.exe'
$currentSources=@(Get-ChildItem -LiteralPath $CoreDirectory -File -Filter '*.cs' | ForEach-Object {$_.FullName})
& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ /main:ReleasedSpeciesBaitTests ('/out:'+$currentExe) $currentSources $tests
if($LASTEXITCODE -ne 0){throw 'The combined player test failed to compile.'}
Invoke-JournalTest $currentExe @($journal)
Invoke-JournalTest $currentExe @('nine',$nineNew)
Invoke-JournalTest $currentExe @('nine',$nineUpgraded)
foreach($pair in @(@('player',$oldExe),@('nine',$nineExe),@('nine-skill',$skillExe))){
    $backups=@(Get-ChildItem -LiteralPath $run -File -Filter ($pair[0]+'.before-11-species.*.fltj'))
    if($backups.Count -ne 1){throw 'Expected exactly one dedicated rollback copy for each input file.'}
    Invoke-JournalTest $pair[1] @('verify',$backups[0].FullName)
}
Write-Output "PASS original seven and both nine-species journals and their verified rollback copies: $run"
