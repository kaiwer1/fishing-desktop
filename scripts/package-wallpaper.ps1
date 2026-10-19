$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $projectRoot
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Web build failed.' }
    $artifactFolder = Join-Path $projectRoot 'artifacts'
    New-Item -ItemType Directory -Path $artifactFolder -Force | Out-Null
    $projectVersion = (Get-Content -LiteralPath (Join-Path $projectRoot 'package.json') -Raw | ConvertFrom-Json).version
    $packagePath = Join-Path $artifactFolder ("fishing-desktop-{0}-lively.zip" -f $projectVersion)
    Compress-Archive -Path (Join-Path $projectRoot 'dist/*') -DestinationPath $packagePath -Force
    Get-Item -LiteralPath $packagePath | Select-Object FullName, Length
}
finally {
    Pop-Location
}
