# Installe ou met à jour le plugin AI Race Engineer dans SimHub (dernière release GitHub).
#
#   irm https://raw.githubusercontent.com/IIPoliII/simhub-ai-race-engineer/main/install.ps1 | iex
#
# SimHub installé ailleurs ?  $env:SIMHUB_DIR = "D:\SimHub"  avant la commande.

& {
    $ErrorActionPreference = 'Stop'
    $repo = 'IIPoliII/simhub-ai-race-engineer'
    $dllName = 'AIRaceEngineer.dll'
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    # 1. Trouver SimHub
    $candidates = @(
        $env:SIMHUB_DIR,
        $env:SIMHUB_INSTALL_PATH,
        (Get-ItemProperty 'HKCU:\Software\SimHub' -ErrorAction SilentlyContinue).InstallDirectory,
        "${env:ProgramFiles(x86)}\SimHub",
        "$env:ProgramFiles\SimHub"
    )
    $simhub = $candidates | Where-Object { $_ -and (Test-Path (Join-Path $_ 'SimHubWPF.exe')) } | Select-Object -First 1
    if (-not $simhub) { throw "SimHub introuvable. Indique son dossier avec `$env:SIMHUB_DIR = 'D:\SimHub' puis relance." }
    $simhub = (Resolve-Path $simhub).Path
    Write-Host "SimHub : $simhub"

    # 2. Télécharger la dernière version
    $tmpDir = Join-Path $env:TEMP 'AIRaceEngineer-install'
    New-Item -ItemType Directory -Force $tmpDir | Out-Null
    $tmp = Join-Path $tmpDir $dllName
    Write-Host "Téléchargement de la dernière version..."
    Invoke-WebRequest "https://github.com/$repo/releases/latest/download/$dllName" -OutFile $tmp -UseBasicParsing
    $new = [Reflection.AssemblyName]::GetAssemblyName($tmp)
    if ($new.Name -ne 'AIRaceEngineer') { throw "Le fichier téléchargé n'est pas le plugin attendu." }
    Write-Host "Version $($new.Version)"

    # 3. SimHub doit être fermé
    if (Get-Process SimHubWPF -ErrorAction SilentlyContinue) {
        Write-Host "Ferme SimHub pour continuer..." -ForegroundColor Yellow
        while (Get-Process SimHubWPF -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 1 }
    }

    # 4. Copier (Program Files demande les droits admin)
    $target = Join-Path $simhub $dllName
    $copy = "Remove-Item '$target.old' -Force -ErrorAction SilentlyContinue; Copy-Item '$tmp' '$target' -Force"
    try {
        Invoke-Expression $copy
    } catch {
        Write-Host "Autorisation administrateur demandée pour écrire dans $simhub..."
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes("`$ErrorActionPreference='Stop'; $copy"))
        $p = Start-Process powershell -Verb RunAs -Wait -PassThru -WindowStyle Hidden -ArgumentList "-NoProfile -NonInteractive -EncodedCommand $encoded"
        if ($p.ExitCode -ne 0) { throw "La copie a échoué." }
    }

    $installed = [Reflection.AssemblyName]::GetAssemblyName($target).Version
    if ($installed -ne $new.Version) { throw "La copie a échoué (version installée : $installed)." }

    Write-Host ""
    Write-Host "AI Race Engineer $installed installé." -ForegroundColor Green
    Write-Host "Lance SimHub et accepte d'activer le plugin : il apparaît dans le menu de gauche."
    Write-Host "Les mises à jour se feront ensuite depuis la page du plugin."
}
