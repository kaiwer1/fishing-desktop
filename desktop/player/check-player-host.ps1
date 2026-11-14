param([Parameter(Mandatory=$true)][string]$HostPath)
$ErrorActionPreference = 'Stop'
# Runs in a short-lived Windows PowerShell/.NET Framework process. Reading the
# bytes releases the source before reflection, even if a build is replaced.
$bytes = [IO.File]::ReadAllBytes($HostPath)
$assembly = [Reflection.Assembly]::ReflectionOnlyLoad($bytes)
$attribute = [Reflection.CustomAttributeData]::GetCustomAttributes($assembly) | Where-Object {
    $_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and
    $_.ConstructorArguments.Count -eq 2 -and
    $_.ConstructorArguments[0].Value -eq 'FishingDesktop.PlayerProtocol' -and
    $_.ConstructorArguments[1].Value -eq '1'
}
if (@($attribute).Count -ne 1) { throw 'This game build does not provide player launch protocol 1. Rebuild with -Game.' }
