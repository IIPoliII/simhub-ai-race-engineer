# [Mainteneur] Construit le plugin, le client vocal et l'installateur, puis publie la release GitHub.
# D'habitude, c'est GitHub Actions qui le lance (.github/workflows/build.yml) :
#   1. monte <Version> dans plugin/AIRaceEngineer.csproj et écris ses notes dans CHANGELOG.md et CHANGELOG.fr.md
#   2. commit + push, puis : git tag vX.Y.Z ; git push origin vX.Y.Z   → Actions construit et publie la release.
# En local : .\release.ps1 -BuildOnly construit tout dans plugin\bin\Release sans rien publier.
param(
    [switch]$BuildOnly,
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

$buildArgs = @("-c", "Release")
if ($SimHubDir) { $buildArgs += "-p:SimHubDir=$((Resolve-Path $SimHubDir).Path)" }
dotnet build (Join-Path $PSScriptRoot "plugin\AIRaceEngineer.csproj") @buildArgs
if ($LASTEXITCODE -ne 0) { throw "La compilation du plugin a échoué." }

$out = Join-Path $PSScriptRoot "plugin\bin\Release"
$work = Join-Path ([IO.Path]::GetTempPath()) "AIRaceEngineer-build-$version"
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null

# client.zip : uniquement les fichiers suivis par git (ni .venv, ni modèles, ni secrets)
$zip = Join-Path $out "client.zip"
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

$dll = Join-Path $out "AIRaceEngineer.dll"
$msi = Join-Path $out "AIRaceEngineer-Setup.msi"
$exe = Join-Path $out "AIRaceEngineer-Setup.exe"
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

# AIRaceEngineer-Setup.exe : demande la langue (celle de Windows par défaut) et lance le MSI qu'il contient
dotnet build (Join-Path $PSScriptRoot "installer\Setup\Setup.csproj") -c Release "-p:Version=$version" "-p:Msi=$msi" -o (Join-Path $work "setup")
if ($LASTEXITCODE -ne 0) { throw "La compilation de AIRaceEngineer-Setup.exe a échoué." }
Copy-Item (Join-Path $work "setup\AIRaceEngineer-Setup.exe") $exe -Force

$assets = @($dll, $zip, $exe, $msi, (Join-Path $PSScriptRoot "Installer-AIRaceEngineer.cmd"))
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
$body = "## English`n`n" + (Get-Notes "CHANGELOG.md") + "`n`n## Français`n`n" + (Get-Notes "CHANGELOG.fr.md") +
    "`n`n**Installation** : ``AIRaceEngineer-Setup.exe`` (English / Français). " +
    "``AIRaceEngineer-Setup.msi`` for silent deployment / pour un déploiement silencieux."
[IO.File]::WriteAllText($notes, $body, (New-Object Text.UTF8Encoding $false))

gh release create "v$version" @assets --title "v$version" --notes-file $notes
if ($LASTEXITCODE -ne 0) { throw "La publication de la release a échoué." }
