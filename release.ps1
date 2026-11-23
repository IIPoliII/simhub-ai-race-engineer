# [Mainteneur] Construit le plugin, le client vocal et l'installateur, puis publie la release GitHub.
# D'habitude, c'est GitHub Actions qui le lance (.github/workflows/build.yml) :
#   1. monte <Version> dans plugin/AIRaceEngineer.csproj et la même dans client/pyproject.toml, lance « uv lock » dans client,
#      et écris les notes dans CHANGELOG.md et CHANGELOG.fr.md
#   2. commit + push, puis : git tag vX.Y.Z ; git push origin vX.Y.Z   → Actions construit et publie la release.
# En local : .\release.ps1 -BuildOnly construit tout dans plugin\bin\Release sans rien publier.
# -PublishOnly publie les fichiers déjà construits dans plugin\bin\Release (le job « publish » de GitHub Actions).
param(
    [switch]$BuildOnly,
    [switch]$PublishOnly,
    [string]$SimHubDir   # dossier des DLL de SimHub (par défaut : l'installation de SimHub)
)
$ErrorActionPreference = "Stop"
[xml]$proj = Get-Content (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Version introuvable dans le csproj." }
if (-not $BuildOnly -and -not (Select-String -Path (Join-Path $PSScriptRoot "CHANGELOG.md") -SimpleMatch "## [$version]" -Quiet)) {
    throw "Pas de section [$version] dans CHANGELOG.md : écris les notes de version avant de publier."
}
if ($env:GITHUB_REF_TYPE -eq "tag" -and $env:GITHUB_REF_NAME -ne "v$version") {
    throw "Le tag $env:GITHUB_REF_NAME ne correspond pas à la version du csproj ($version)."
}
# Le client vocal porte la même version (pas réécrite ici : uv.lock la contient, il deviendrait périmé)
$pyproject = Get-Content (Join-Path $PSScriptRoot "client\pyproject.toml") -Raw -Encoding UTF8
$clientVersion = if ($pyproject -match '(?m)^version\s*=\s*"([^"]+)"') { $Matches[1] }
if ($clientVersion -ne $version) {
    $msg = "client/pyproject.toml est en version $clientVersion, le csproj en $version : mets la même, puis « uv lock » dans client."
    if ($BuildOnly -and $env:GITHUB_REF_TYPE -ne "tag") { Write-Warning $msg } else { throw $msg }
}

if ($BuildOnly -and $PublishOnly) { throw "-BuildOnly et -PublishOnly s'excluent." }

$out = Join-Path $PSScriptRoot "plugin\bin\Release"
$work = Join-Path ([IO.Path]::GetTempPath()) "AIRaceEngineer-build-$version"
$dll = Join-Path $out "AIRaceEngineer.dll"
$zip = Join-Path $out "client.zip"
$msi = Join-Path $out "AIRaceEngineer-Setup.msi"
$exe = Join-Path $out "AIRaceEngineer-Setup.exe"
$assets = @($dll, $zip, $exe, $msi, (Join-Path $PSScriptRoot "Installer-AIRaceEngineer.cmd"))
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null

if (-not $PublishOnly) {
# uv.lock fige les dépendances du client chez les utilisateurs : il doit être suivi par git (client.zip et le MSI
# ne prennent que ces fichiers) et correspondre à pyproject.toml
if ((git -C $PSScriptRoot ls-files client/uv.lock) -ne "client/uv.lock") { throw "client/uv.lock n'est pas suivi par git : git add client/uv.lock" }
$uv = (Get-Command uv -ErrorAction SilentlyContinue).Source
if (-not $uv) { $uv = "$env:USERPROFILE\.local\bin\uv.exe" }
if (-not (Test-Path $uv)) { throw "uv introuvable : il faut uv pour vérifier client/uv.lock." }
& $uv lock --check --quiet --directory (Join-Path $PSScriptRoot "client")
if ($LASTEXITCODE -ne 0) { throw "client/uv.lock n'est plus à jour : lance « uv lock » dans client, puis commit." }

$buildArgs = @("-c", "Release")
if ($SimHubDir) { $buildArgs += "-p:SimHubDir=$((Resolve-Path $SimHubDir).Path)" }
dotnet build (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj") @buildArgs
if ($LASTEXITCODE -ne 0) { throw "La compilation du plugin a échoué." }

# client.zip : uniquement les fichiers suivis par git (ni .venv, ni modèles, ni secrets)
$stage = Join-Path $work "client"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Push-Location $PSScriptRoot
try {
    git ls-files client | ForEach-Object {
        $dest = Join-Path $stage ($_ -replace '^client/', '')
        New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
        Copy-Item $_ $dest
    }
} finally { Pop-Location }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

# MSI (WiX 5, outil local épinglé dans dotnet-tools.json) : construit en anglais et en français, puis le français est
# intégré au MSI anglais en transformation de langue « 1036 ». Windows l'applique tout seul sur un Windows en français ;
# AIRaceEngineer-Setup.exe, lui, laisse choisir.
function Invoke-Com($obj, [string]$member, [Reflection.BindingFlags]$kind, [object[]]$params) {
    $obj.GetType().InvokeMember($member, $kind, $null, $obj, $params)
}
function Add-LanguageTransform([string]$msi, [string]$mst, [int]$lcid, [string]$template) {
    $call = [Reflection.BindingFlags]::InvokeMethod; $get = [Reflection.BindingFlags]::GetProperty; $set = [Reflection.BindingFlags]::SetProperty
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $db = Invoke-Com $installer OpenDatabase $call @($msi, 1)
    $view = Invoke-Com $db OpenView $call @('INSERT INTO `_Storages` (`Name`, `Data`) VALUES (?, ?)')
    $record = Invoke-Com $installer CreateRecord $call @(2)
    Invoke-Com $record StringData $set @(1, "$lcid") | Out-Null
    Invoke-Com $record SetStream $call @(2, $mst) | Out-Null
    Invoke-Com $view Execute $call @(, $record) | Out-Null
    Invoke-Com $view Close $call @() | Out-Null
    $summary = Invoke-Com $db SummaryInformation $get @(1)
    Invoke-Com $summary Property $set @(7, $template) | Out-Null   # 7 = Template : plateforme;langues
    Invoke-Com $summary Persist $call @() | Out-Null
    Invoke-Com $db Commit $call @() | Out-Null
    foreach ($o in $summary, $record, $view, $db, $installer) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($o) }
}

$productCode = [guid]::NewGuid().ToString("B").ToUpper()   # le même pour les deux langues : la transformation ne change que les textes
Push-Location $PSScriptRoot
try {
    dotnet tool restore | Out-Null
    dotnet wix extension add WixToolset.UI.wixext/5.0.2 WixToolset.Util.wixext/5.0.2 | Out-Null
    foreach ($culture in "en-US", "fr-FR") {
        dotnet wix build (Join-Path $PSScriptRoot "installer\AIRaceEngineer.wxs") -arch x64 -culture $culture `
            -loc (Join-Path $PSScriptRoot "installer\$culture.wxl") -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext `
            -d "Version=$version" -d "ProductCode=$productCode" -d "Root=$PSScriptRoot" -d "Dll=$dll" -d "ClientSrc=$stage" `
            -o (Join-Path $work "$culture.msi")
        if ($LASTEXITCODE -ne 0) { throw "La construction du MSI ($culture) a échoué." }
    }
    $mst = Join-Path $work "fr-FR.mst"
    dotnet wix msi transform (Join-Path $work "en-US.msi") (Join-Path $work "fr-FR.msi") -t language -out $mst
    if ($LASTEXITCODE -ne 0) { throw "La transformation française a échoué." }
} finally { Pop-Location }
Copy-Item (Join-Path $work "en-US.msi") $msi -Force
Add-LanguageTransform $msi $mst 1036 "x64;1033,1036"

# AIRaceEngineer-Setup.exe : demande la langue (celle déjà installée, sinon celle de Windows) et lance le MSI qu'il contient
dotnet build (Join-Path $PSScriptRoot "installer\Setup\Setup.csproj") -c Release "-p:Version=$version" "-p:Msi=$msi" -o (Join-Path $work "setup")
if ($LASTEXITCODE -ne 0) { throw "La compilation de AIRaceEngineer-Setup.exe a échoué." }
Copy-Item (Join-Path $work "setup\AIRaceEngineer-Setup.exe") $exe -Force
}

foreach ($a in $assets) { if (-not (Test-Path $a)) { throw "Fichier manquant : $a" } }
if ($BuildOnly) { $assets | ForEach-Object { Write-Host "  $_" }; return }

# Notes de la release : la section de cette version dans CHANGELOG.md (anglais) et CHANGELOG.fr.md (français)
function Get-Notes([string]$file) {
    $lines = Get-Content (Join-Path $PSScriptRoot $file) -Encoding UTF8
    $start = [array]::FindIndex($lines, [Predicate[string]] { param($l) $l.StartsWith("## [$version]") })
    if ($start -lt 0) { throw "Pas de section [$version] dans $file : écris les notes de version avant de publier." }
    $end = [array]::FindIndex($lines, $start + 1, [Predicate[string]] { param($l) $l.StartsWith("## ") -or $l -match '^\[[^\]]+\]: ' })
    if ($end -lt 0) { $end = $lines.Count }
    ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
}
$notes = Join-Path $work "release-notes.md"
# Une ligne d'installation dans chaque langue, sous sa propre section
$body = "## English`n`n" + (Get-Notes "CHANGELOG.md") +
    "`n`n**Install**: download ``AIRaceEngineer-Setup.exe`` (English or French). ``AIRaceEngineer-Setup.msi`` is for " +
    "silent deployment; ``Installer-AIRaceEngineer.cmd`` runs the one-line installer. " +
    "Already installed? Click **Install** in the update banner at the top of the plugin page in SimHub " +
    "(on versions before 0.5.0 the page is in French: **Installer**)." +
    "`n`n## Français`n`n" + (Get-Notes "CHANGELOG.fr.md") +
    "`n`n**Installation** : télécharge ``AIRaceEngineer-Setup.exe`` (français ou anglais). ``AIRaceEngineer-Setup.msi`` " +
    "sert au déploiement silencieux ; ``Installer-AIRaceEngineer.cmd`` lance l'installation en une ligne. " +
    "Déjà installé ? Clique sur **Installer** dans le bandeau de mise à jour, en haut de la page du plugin dans SimHub."
[IO.File]::WriteAllText($notes, $body, (New-Object Text.UTF8Encoding $false))

gh release create "v$version" @assets --title "v$version" --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw "La publication de la release a échoué." }
