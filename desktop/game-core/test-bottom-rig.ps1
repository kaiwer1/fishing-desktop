param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../diagnostics/dual-window/private-path.ps1')
$root=Get-DualWindowPrivateRoot $OutputDirectory
$run=Join-Path $root ('bottom-rig-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$exe=Join-Path $run 'BottomRigSimulationTests.exe'
$sources=@('FloatStillWaterGeometry.cs','FloatBiteSimulation.cs','BottomSimpleRig.cs','BottomRigSimulation.cs','tests/BottomRigSimulationTests.cs') |
    ForEach-Object {Join-Path $PSScriptRoot $_}
$build=@(& $compiler /nologo /utf8output /codepage:65001 /target:exe /platform:x64 /warnaserror+ ('/out:'+$exe) @sources)
$buildExit=$LASTEXITCODE
$build | Set-Content -LiteralPath (Join-Path $run 'build-output.txt') -Encoding utf8
if($buildExit -ne 0){$build | Out-Host;throw 'Bottom rig tests did not compile.'}
$output=@(& $exe)
$exitCode=$LASTEXITCODE
$output | Set-Content -LiteralPath (Join-Path $run 'test-output.txt') -Encoding utf8
$passed=@($output | Where-Object {$_ -like 'PASS *'})
$failed=@($output | Where-Object {$_ -like 'FAIL *'})
$report=[pscustomobject]@{passed=($exitCode -eq 0 -and $passed.Count -eq 20 -and $failed.Count -eq 0);
    passedCount=$passed.Count;checks=$passed;failures=$failed;outputDirectory=$run}
$report | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding utf8
if(-not $report.passed){$output | Out-Host;throw 'Bottom rig checks failed.'}
$report | ConvertTo-Json -Depth 3
