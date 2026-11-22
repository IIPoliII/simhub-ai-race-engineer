# [Mainteneur] Compile le plugin et publie une release GitHub : DLL, client vocal, installateurs MSI (FR/EN) et installateur en une ligne.
# 1. Monte <Version> dans plugin/AIRaceEngineer.csproj   2. commit + push   3. .\release.ps1
param([switch]$MsiOnly)  # -MsiOnly : construit tout sans publier la release
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

# Installateurs MSI, français et anglais (WiX 5, outil local épinglé dans dotnet-tools.json)
$dll = Join-Path $out "AIRaceEngineer.dll"
$msis = @()
Push-Location $PSScriptRoot
try {
    dotnet tool restore | Out-Null
    dotnet wix extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 | Out-Null
    foreach ($lang in @{ code = "fr"; culture = "fr-FR" }, @{ code = "en"; culture = "en-US" }) {
        $msi = Join-Path $out "AIRaceEngineer-Setup-$($lang.code).msi"
        dotnet wix build (Join-Path $PSScriptRoot "installer\AIRaceEngineer.wxs") -arch x64 -culture $lang.culture `
            -loc (Join-Path $PSScriptRoot "installer\$($lang.culture).wxl") -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
            -d "Version=$version" -d "Root=$PSScriptRoot" -d "Dll=$dll" -d "ClientSrc=$stage" -o $msi
        if ($LASTEXITCODE -ne 0) { throw "La construction du MSI ($($lang.culture)) a échoué." }
        $msis += $msi
    }
} finally { Pop-Location }

if ($MsiOnly) { $msis | ForEach-Object { Write-Host "MSI : $_" }; return }
$cmd = Join-Path $PSScriptRoot "Installer-AIRaceEngineer.cmd"
gh release create "v$version" $dll $zip @msis $cmd --title "v$version" --generate-notes
