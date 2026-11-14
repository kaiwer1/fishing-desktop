param(
    [Parameter(Mandatory=$true)][string]$BuildDirectory,
    [Parameter(Mandatory=$true)][string]$ArchivePath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-DirectItem([string]$Path) {
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    $cursor = $item
    while ($null -ne $cursor) {
        if (($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Use a direct path without links: $Path"
        }
        if ($cursor -is [IO.FileInfo]) { $cursor = $cursor.Directory }
        else { $cursor = $cursor.Parent }
    }
    return $item
}

$buildRoot = Get-DirectItem $BuildDirectory
if ($buildRoot -isnot [IO.DirectoryInfo]) { throw 'BuildDirectory must be an existing game build.' }
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ArchivePath)
if ([IO.Path]::GetExtension($destination) -ine '.zip') { throw 'ArchivePath must be a new .zip file.' }
$outputFolder = Get-DirectItem ([IO.Path]::GetDirectoryName($destination))
if ($outputFolder -isnot [IO.DirectoryInfo] -or (Test-Path -LiteralPath $destination)) {
    throw 'Use an existing output folder and a new ZIP name.'
}
$player = Join-Path $PSScriptRoot '../desktop/player'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The x64 .NET Framework compiler is unavailable.' }

# Every archive member has an explicit origin. A used game build can contain a
# profile, browser cache, screenshots and test results alongside the executable.
$members = [ordered]@{}
$hostFiles = @('DualWindowHost.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll',
    'WebView2Loader.dll','WEBVIEW2-LICENSE.txt')
$pageFiles = @('game.html','page.mjs','lease-session.mjs','cast-input.mjs','flight-view.mjs','bite-view.mjs',
    'fight-view.mjs','shop-view.mjs','equipment-view.mjs','equipment-panel.mjs','spares-view.mjs','spares-panel.mjs',
    'camp-view.mjs','camp-panel.mjs','water-aim.mjs','water-view.mjs','spot-menu.mjs','wardrobe.mjs','fish-illustration.mjs','scene-presentation.mjs','game-scene.mjs','THREE-LICENSE.txt')
foreach ($name in $hostFiles) { $members["app/$name"] = Join-Path $buildRoot.FullName $name }
foreach ($name in $pageFiles) { $members["app/page/$name"] = Join-Path $buildRoot.FullName "page/$name" }
$members['Readme-zh-CN.txt'] = Join-Path $player 'Readme-zh-CN.txt'
foreach ($path in $members.Values) {
    $item = Get-DirectItem $path
    if ($item -isnot [IO.FileInfo] -or $item.Length -eq 0) { throw "Missing game package file: $path" }
}
# Check local ES-module imports against the explicit package list. Checking
# each module also covers indirect dependencies introduced by a new panel.
foreach ($name in $pageFiles) {
    if (-not $name.EndsWith('.mjs')) { continue }
    $code = [IO.File]::ReadAllText($members["app/page/$name"])
    $pattern = '(?:\bfrom\s+|\bimport\s*(?:\(\s*)?)[''"]\./([^''"]+)[''"]'
    foreach ($import in [Regex]::Matches($code,$pattern)) {
        $dependency = $import.Groups[1].Value
        if ($pageFiles -cnotcontains $dependency) { throw "$name imports an unpackaged module: $dependency" }
    }
}
$windowsPowerShell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
& $windowsPowerShell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $player 'check-player-host.ps1') -HostPath $members['app/DualWindowHost.exe']
if ($LASTEXITCODE -ne 0) { throw 'The selected game host failed its player protocol check.' }

$scratch = Join-Path $outputFolder.FullName ('fishing-preview-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -Path $scratch -ItemType Directory -ErrorAction Stop | Out-Null
$launcher = Join-Path $scratch 'FishingDesktop.exe'
$temporaryZip = Join-Path $scratch 'package.zip'
try {
    $argsForCompiler = @('/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/optimize+','/warnaserror+',
        '/r:System.Windows.Forms.dll','/r:System.Drawing.dll',('/win32manifest:' + (Join-Path $player 'launcher.manifest')),
        ('/out:' + $launcher),(Join-Path $player 'FishingDesktop.cs'))
    & $compiler @argsForCompiler
    if ($LASTEXITCODE -ne 0) { throw 'The player launcher did not compile.' }
    $members['FishingDesktop.exe'] = $launcher
    $stream = [IO.File]::Open($temporaryZip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($entry in $members.GetEnumerator()) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    # Publish only a completed archive. File.Move also rejects a destination
    # created by another packaging process after our initial check.
    [IO.File]::Move($temporaryZip, $destination)
    $info = Get-Item -LiteralPath $destination
    [PSCustomObject]@{ ArchivePath=$info.FullName; Bytes=$info.Length; Files=$members.Count; Sha256=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
} finally {
    foreach ($path in @($launcher,$temporaryZip)) {
        if ([IO.File]::Exists($path)) { [IO.File]::Delete($path) }
    }
    [IO.Directory]::Delete($scratch)
}
