# Installs a downloaded SimHub release on the CI runner (silently, in a temporary folder) and copies its DLLs to $Out.
# The repository never contains SimHub's DLLs: they come from SimHub's own GitHub releases, only to compile against
# them and check compatibility. The installer runs only if it carries a valid signature from SimHub's publisher.
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Download,   # folder holding the release zip
    [Parameter(Mandatory)][string]$Out
)
$ErrorActionPreference = "Stop"
# SimHub's code signing certificate (SimHubWPF.exe and its installer)
$Publisher = "O=Nicolas Guilbert"

$zip = Get-ChildItem $Download -Filter *.zip | Select-Object -First 1
if (-not $zip) { throw "No SimHub zip in $Download." }
$tmp = Join-Path $env:RUNNER_TEMP "simhub-$Version"
Expand-Archive $zip.FullName -DestinationPath "$tmp\zip" -Force
$setup = Get-ChildItem "$tmp\zip" -Filter *.exe -Recurse | Select-Object -First 1
if (-not $setup) { throw "No installer in the SimHub $Version zip." }

$sig = Get-AuthenticodeSignature $setup.FullName
if ($sig.Status -ne "Valid" -or $sig.SignerCertificate.Subject -notlike "*$Publisher*") {
    throw "$($setup.Name) is not signed by SimHub's publisher ($($sig.Status), $($sig.SignerCertificate.Subject)): not running it."
}

# Inno Setup, silent, into a folder of our own
$dir = Join-Path $tmp "install"
$p = Start-Process $setup.FullName -PassThru -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/NOICONS", "/DIR=`"$dir`""
if (-not $p.WaitForExit(20 * 60 * 1000)) { $p.Kill(); throw "The SimHub installer did not finish within 20 minutes." }
if ($p.ExitCode -ne 0) { throw "The SimHub installer failed (code $($p.ExitCode))." }
Get-Process SimHubWPF -ErrorAction SilentlyContinue | Stop-Process -Force

New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item "$dir\*.dll" $Out
foreach ($dll in "SimHub.Plugins", "GameReaderCommon", "SimHub.Logging", "Newtonsoft.Json", "log4net") {
    if (-not (Test-Path "$Out\$dll.dll")) { throw "$dll.dll is missing from SimHub $Version." }
}
Set-Content "$Out\simhub-version.txt" $Version
Write-Host "SimHub $Version : $((Get-ChildItem $Out -Filter *.dll).Count) DLLs in $Out"
