# [Développeurs] Compile le plugin depuis les sources et le copie dans le dossier de SimHub.
# [Developers] Builds the plugin from source and copies it into SimHub's folder.
# Usage :  .\dev-install.ps1            (SimHub dans C:\Program Files (x86)\SimHub)
#          .\dev-install.ps1 -SimHubDir "D:\SimHub"
param(
    [string]$SimHubDir = $(if ($env:SIMHUB_INSTALL_PATH) { $env:SIMHUB_INSTALL_PATH } else { "C:\Program Files (x86)\SimHub" })
)
$ErrorActionPreference = "Stop"
# Messages en français si Windows est en français, sinon en anglais
$fr = (Get-UICulture).TwoLetterISOLanguageName -eq "fr"
function T([string]$f, [string]$e) { if ($fr) { $f } else { $e } }

if (-not (Test-Path (Join-Path $SimHubDir "SimHubWPF.exe"))) {
    throw (T "SimHub introuvable dans '$SimHubDir'. Passe le bon dossier avec -SimHubDir." "SimHub not found in '$SimHubDir'. Pass the right folder with -SimHubDir.")
}
$SimHubDir = (Resolve-Path -LiteralPath $SimHubDir).Path   # chemin complet, aussi pour la copie en élevé
if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
    throw (T "Ferme SimHub avant d'installer le plugin (la DLL est verrouillée tant qu'il tourne)." "Close SimHub before installing the plugin (its DLL stays locked while SimHub runs).")
}

$project = Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj"
dotnet build $project -c Release -p:SimHubDir="$SimHubDir"
if ($LASTEXITCODE -ne 0) { throw (T "La compilation a échoué." "The build failed.") }

$dll = Join-Path $PSScriptRoot "plugin\bin\Release\AIRaceEngineer.dll"
$target = Join-Path $SimHubDir "AIRaceEngineer.dll"
$hash = (Get-FileHash -LiteralPath $dll).Hash
try {
    Copy-Item -LiteralPath $dll -Destination $SimHubDir -Force
} catch {
    # Program Files demande les droits admin : on relance juste la copie en élevé (chemins échappés : une ' ne casse rien)
    Write-Host (T "Droits administrateur nécessaires pour copier dans $SimHubDir..." "Administrator rights needed to copy to $SimHubDir...")
    $q = { "'" + [Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent($args[0]) + "'" }
    $copy = "`$ErrorActionPreference='Stop'; Copy-Item -LiteralPath $(& $q $dll) -Destination $(& $q $SimHubDir) -Force"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($copy))
    $p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList "-NoProfile -NonInteractive -EncodedCommand $encoded"
    if ($p.ExitCode -ne 0) { throw (T "La copie a échoué." "Copying the plugin failed.") }
}

# La DLL en place doit être celle qu'on vient de compiler (une ancienne peut déjà s'y trouver)
if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $hash) {
    Write-Host (T "OK : plugin installé. Lance SimHub et accepte d'activer « AI Race Engineer »." "OK: plugin installed. Start SimHub and say yes when it offers to enable 'AI Race Engineer'.") -ForegroundColor Green
} else {
    throw (T "La copie a échoué." "Copying the plugin failed.")
}
