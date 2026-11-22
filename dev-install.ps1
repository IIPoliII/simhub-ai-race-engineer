# [Développeurs] Compile le plugin depuis les sources et le copie dans le dossier de SimHub.
# Usage :  .\dev-install.ps1            (SimHub dans C:\Program Files (x86)\SimHub)
#          .\dev-install.ps1 -SimHubDir "D:\SimHub"
param(
    [string]$SimHubDir = $(if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { "C:\Program Files (x86)\SimHub" })
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $SimHubDir "SimHubWPF.exe"))) {
    throw "SimHub introuvable dans '$SimHubDir'. Passe le bon dossier avec -SimHubDir."
}
if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
    throw "Ferme SimHub avant d'installer le plugin (la DLL est verrouillée tant qu'il tourne)."
}

$project = Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj"
dotnet build $project -c Release -p:SimHubDir="$SimHubDir"
if ($LASTEXITCODE -ne 0) { throw "La compilation a échoué." }

$dll = Join-Path $PSScriptRoot "plugin\bin\Release\AIRaceEngineer.dll"
try {
    Copy-Item $dll $SimHubDir -Force
} catch {
    # Program Files demande les droits admin : on relance juste la copie en élevé
    Write-Host "Droits administrateur nécessaires pour copier dans $SimHubDir..."
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile -Command Copy-Item -LiteralPath '$dll' -Destination '$SimHubDir' -Force"
}

if (Test-Path (Join-Path $SimHubDir "AIRaceEngineer.dll")) {
    Write-Host "OK : plugin installé. Lance SimHub et accepte d'activer 'AI Race Engineer'." -ForegroundColor Green
} else {
    throw "La copie a échoué."
}
