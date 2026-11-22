# [Mainteneur] Compile le plugin et publie une release GitHub : DLL, client vocal et installateur.
# 1. Monte <Version> dans plugin/AIRaceEngineer.csproj   2. commit + push   3. .\release.ps1
$ErrorActionPreference = "Stop"
[xml]$proj = Get-Content (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Version introuvable dans le csproj." }

dotnet build (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

# client.zip : uniquement les fichiers suivis par git (ni .venv, ni modèles, ni secrets)
$out = Join-Path $PSScriptRoot "plugin\bin\Release"
$zip = Join-Path $out "client.zip"
$stage = Join-Path ([IO.Path]::GetTempPath()) "AIRaceEngineer-client-$version"
Remove-Item $stage, $zip -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $stage | Out-Null
Push-Location $PSScriptRoot
try {
    git ls-files client | ForEach-Object {
        $dest = Join-Path $stage ($_ -replace '^client/', '')
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Copy-Item $_ $dest
    }
} finally { Pop-Location }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

$dll = Join-Path $out "AIRaceEngineer.dll"
$cmd = Join-Path $PSScriptRoot "Installer-AIRaceEngineer.cmd"
gh release create "v$version" $dll $zip $cmd --title "v$version" --generate-notes
