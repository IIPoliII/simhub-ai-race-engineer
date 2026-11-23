# Installe ou met à jour le plugin AI Race Engineer dans SimHub (dernière release GitHub).
# Installs or updates the AI Race Engineer plugin in SimHub (latest GitHub release).
#
#   irm https://raw.githubusercontent.com/IIPoliII/simhub-ai-race-engineer/main/install.ps1 | iex
#
# SimHub installé ailleurs ?  $env:SIMHUB_DIR = "D:\SimHub"  avant la commande.
# SimHub installed elsewhere?  $env:SIMHUB_DIR = "D:\SimHub"  before the command.

& {
    $ErrorActionPreference = 'Stop'
    $repo = 'IIPoliII/simhub-ai-race-engineer'
    $dllName = 'AIRaceEngineer.dll'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    # Messages en français si Windows est en français, sinon en anglais
    $lang = if ((Get-UICulture).TwoLetterISOLanguageName -eq 'fr') { 'fr' } else { 'en' }
    function T([string]$fr, [string]$en) { if ($lang -eq 'fr') { $fr } else { $en } }

    # 1. Trouver SimHub
    $candidates = @(
        $env:SIMHUB_DIR,
        $env:SIMHUB_INSTALL_PATH,
        (Get-ItemProperty 'HKCU:\Software\SimHub' -ErrorAction SilentlyContinue).InstallDirectory,
        "${env:ProgramFiles(x86)}\SimHub",
        "$env:ProgramFiles\SimHub"
    )
    $simhub = $candidates | Where-Object { $_ -and (Test-Path (Join-Path $_ 'SimHubWPF.exe')) } | Select-Object -First 1
    if (-not $simhub) {
        throw (T "SimHub introuvable. Indique son dossier avec `$env:SIMHUB_DIR = 'D:\SimHub' puis relance." `
                 "SimHub not found. Set its folder with `$env:SIMHUB_DIR = 'D:\SimHub', then run this again.")
    }
    $simhub = (Resolve-Path $simhub).Path
    Write-Host (T "SimHub : $simhub" "SimHub: $simhub")

    # 2. Télécharger la dernière version
    $tmpDir = Join-Path $env:TEMP 'AIRaceEngineer-install'
    New-Item -ItemType Directory -Force $tmpDir | Out-Null
    $tmp = Join-Path $tmpDir $dllName
    Write-Host (T "Téléchargement de la dernière version..." "Downloading the latest version...")
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/$dllName" -OutFile $tmp -UseBasicParsing
    $new = [Reflection.AssemblyName]::GetAssemblyName($tmp)
    if ($new.Name -ne 'AIRaceEngineer') { throw (T "Le fichier téléchargé n'est pas le plugin attendu." "The downloaded file is not the expected plugin.") }
    Write-Host "Version $($new.Version)"

    # 3. Client vocal, dans le profil utilisateur (pas besoin des droits admin)
    $dataDir = Join-Path $env:LOCALAPPDATA 'AIRaceEngineer'
    $clientDir = Join-Path $dataDir 'client'
    $zip = Join-Path $tmpDir 'client.zip'
    Write-Host (T "Installation du client vocal dans $clientDir..." "Installing the voice client in $clientDir...")
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/client.zip" -OutFile $zip -UseBasicParsing
    New-Item -ItemType Directory -Force $clientDir | Out-Null
    Expand-Archive $zip -DestinationPath $clientDir -Force   # garde .venv et les modèles déjà téléchargés

    $uv = @("$env:USERPROFILE\.local\bin\uv.exe", "$env:USERPROFILE\.cargo\bin\uv.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $uv) { $uv = (Get-Command uv -ErrorAction SilentlyContinue).Source }
    if (-not $uv) {
        Write-Host (T "Installation de uv (gestionnaire Python d'Astral)..." "Installing uv (Astral's Python manager)...")
        powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://astral.sh/uv/install.ps1 | iex" | Out-Host
        $uv = "$env:USERPROFILE\.local\bin\uv.exe"
        if (-not (Test-Path $uv)) { throw (T "uv n'a pas pu être installé." "uv could not be installed.") }
    }

    Push-Location $clientDir
    $oldLang = $env:AIR_LANG
    $env:AIR_LANG = $lang   # messages du client vocal dans la même langue
    # uv.lock livré avec le client : les versions testées, sans nouvelle résolution (les releases avant 0.5.0 n'en ont pas)
    $frozen = @(); if (Test-Path (Join-Path $clientDir 'uv.lock')) { $frozen = @('--frozen') }
    try {
        Write-Host (T "Installation des dépendances Python (première fois : quelques minutes)..." "Installing the Python dependencies (a few minutes the first time)...")
        & $uv sync @frozen --no-progress
        if ($LASTEXITCODE -ne 0) { throw (T "uv sync a échoué." "uv sync failed.") }
        Write-Host (T "Téléchargement de la voix et de la reconnaissance vocale (~1 Go la première fois)..." "Downloading the voice and speech recognition (~1 GB the first time)...")
        & $uv run @frozen --no-progress engineer.py --prepare
        if ($LASTEXITCODE -ne 0) { throw (T "Le téléchargement des modèles a échoué." "Downloading the models failed.") }
    } finally { Pop-Location; $env:AIR_LANG = $oldLang }

    # 4. SimHub doit être fermé pour remplacer le plugin
    if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
        Write-Host (T "Ferme SimHub pour continuer..." "Close SimHub to continue...") -ForegroundColor Yellow
        while (Get-Process SimHubWPF -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    }

    # 5. Copier : sans admin si le dossier de SimHub le permet (cas habituel), sinon avec autorisation
    $target = Join-Path $simhub $dllName
    try {
        Remove-Item -LiteralPath "$target.old" -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath $tmp -Destination $target -Force
    } catch {
        Write-Host (T "Autorisation administrateur demandée pour écrire dans $simhub..." "Asking for administrator permission to write to $simhub...")
        # Chemins échappés : une apostrophe (D'Amour, O'Brien...) ne casse pas la commande
        $q = { "'" + [Management.Automation.Language.CodeGeneration]::EscapeSingleQuotedStringContent($args[0]) + "'" }
        $copy = "`$ErrorActionPreference='Stop'; Remove-Item -LiteralPath $(& $q "$target.old") -Force -ErrorAction SilentlyContinue; " +
                "Copy-Item -LiteralPath $(& $q $tmp) -Destination $(& $q $target) -Force"
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($copy))
        $p = Start-Process powershell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -NonInteractive -EncodedCommand $encoded"
        if ($p.ExitCode -ne 0) { throw (T "La copie a échoué." "Copying the plugin failed.") }
    }

    $installed = [Reflection.AssemblyName]::GetAssemblyName($target).Version
    if ($installed -ne $new.Version) { throw (T "La copie a échoué (version installée : $installed)." "Copying the plugin failed (installed version: $installed).") }

    Write-Host ""
    Write-Host (T "AI Race Engineer $installed installé." "AI Race Engineer $installed installed.") -ForegroundColor Green
    Write-Host (T "Lance SimHub et accepte d'activer le plugin : il apparaît dans le menu de gauche," `
                  "Start SimHub and say yes when it offers to enable the plugin: it shows up in the left menu.")
    Write-Host (T "règle-le là (IA, langue, voix, annonces) et associe le push-to-talk dans Controls and events." `
                  "Set it up there (AI, language, voice, callouts) and bind push-to-talk in Controls and events.")
    Write-Host (T "Le client vocal démarre tout seul avec SimHub. Les mises à jour se font depuis la page du plugin." `
                  "The voice client starts on its own with SimHub. Update it later from the plugin page.")
}
