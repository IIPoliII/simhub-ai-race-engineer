# [Mainteneur] Compile le plugin et publie une release GitHub avec la DLL et l'installateur.
# 1. Monte <Version> dans plugin/AIRaceEngineer.csproj   2. commit + push   3. .\release.ps1
$ErrorActionPreference = "Stop"
[xml]$proj = Get-Content (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Version introuvable dans le csproj." }

dotnet build (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

$dll = Join-Path $PSScriptRoot "plugin\bin\Release\AIRaceEngineer.dll"
$cmd = Join-Path $PSScriptRoot "Installer-AIRaceEngineer.cmd"
gh release create "v$version" $dll $cmd --title "v$version" --generate-notes
