<#
.SYNOPSIS
    Installiert Lotse auf diesem Windows-Rechner: ein Klick im Startmenue statt "dotnet run" im Terminal.

.DESCRIPTION
    Baut Lotse als eigenstaendige Anwendung (self-contained, win-x64) nach %LOCALAPPDATA%\Lotse\app und legt
    einen Starter an. Der Starter startet den Server unsichtbar im Hintergrund, falls er noch nicht laeuft,
    wartet auf /health und oeffnet Lotse im Standardbrowser. Ein zweiter Klick oeffnet nur den Browser.

    Daten liegen danach in %LOCALAPPDATA%\Lotse\data - getrennt vom Programm, sodass ein Update oder
    eine Neuinstallation sie nicht beruehrt. Gibt es dort noch keine Datenbank, wird der bisherige Lernstand
    aus dem Repository (src\Lotse.Web\data) kopiert, samt Schluesselbund fuer den gespeicherten API-Schluessel.
    Das Original bleibt unveraendert liegen.

    Braucht das .NET-SDK nur fuer den Bau. Zum Ausfuehren ist danach nichts weiter noetig.
    Das Skript ist fuer Windows PowerShell 5.1 geschrieben und idempotent: erneut ausfuehren = aktualisieren.

.PARAMETER Port
    Port des lokalen Servers. Standard 5178, derselbe wie bei "dotnet run".

.PARAMETER Autostart
    Startet den Server zusaetzlich bei der Anmeldung an Windows (ohne Browserfenster).

.PARAMETER Uninstall
    Entfernt Programm, Starter und Verknuepfungen. Die Daten bleiben liegen.

.PARAMETER Tailscale
    Macht Lotse auf den eigenen Geraeten im Tailscale-Netz erreichbar (Handy, Laptop), per HTTPS und ohne
    oeffentlichen Server: "tailscale serve" leitet https://<rechner>.<tailnet>.ts.net an den lokalen Port weiter.
    Der Server bleibt an localhost gebunden; nur deshalb darf er den Weiterleitungs-Headern trauen
    (Lotse__Hosting__BehindProxy). Tailscale muss installiert und angemeldet sein (kostenloser Personal-Plan).

.PARAMETER Root
    Installationsordner. Standard: %LOCALAPPDATA%\Lotse (dort liegt auch die Sprachausgabe aus install-piper.ps1).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/install-windows.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/install-windows.ps1 -Autostart
#>
[CmdletBinding()]
param(
    [int]$Port = 5178,
    [switch]$Autostart,
    [switch]$Uninstall,
    [switch]$Tailscale,
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'Lotse'),
    # For tests: build and write files, but create no shortcuts.
    [switch]$NoShortcuts
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $Root 'app'
$dataDir = Join-Path $Root 'data'
$launcher = Join-Path $Root 'Lotse.ps1'
$programs = [Environment]::GetFolderPath('Programs')
$startup = [Environment]::GetFolderPath('Startup')
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcuts = @(
    (Join-Path $programs 'Lotse.lnk'),
    (Join-Path $desktop 'Lotse.lnk'),
    (Join-Path $startup 'Lotse (Hintergrund).lnk')
)

function Stop-Lotse {
    # Only the copy installed here - a "dotnet run" from the repository is left alone.
    Get-Process -Name 'Lotse.Web' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($appDir, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Write-Host "Beende laufendes Lotse (PID $($_.Id)) ..."; $_ | Stop-Process -Force; $_.WaitForExit(10000) | Out-Null }
}

function New-Shortcut([string]$Path, [string]$Arguments, [string]$Description) {
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut($Path)
    $lnk.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$launcher`" $Arguments".Trim()
    $lnk.WorkingDirectory = $Root
    $lnk.IconLocation = "$(Join-Path $appDir 'Lotse.Web.exe'),0"
    $lnk.Description = $Description
    $lnk.WindowStyle = 7 # minimised: no console flashes up
    $lnk.Save()
}

if ($Uninstall) {
    Stop-Lotse
    foreach ($s in $shortcuts) { if (Test-Path $s) { Remove-Item $s -Force } }
    foreach ($p in @($appDir, $launcher)) { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }
    Write-Host "Lotse ist entfernt. Deine Daten liegen weiter in $dataDir - loesche den Ordner selbst, wenn du sie nicht mehr brauchst."
    return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Das .NET-SDK fehlt (dotnet). Installiere es von https://dot.net und starte das Skript erneut.'
}

# ---- 1. Build ----------------------------------------------------------------------------------------------------
Stop-Lotse
New-Item -ItemType Directory -Force -Path $Root | Out-Null
Write-Host "Baue Lotse nach $appDir (das dauert beim ersten Mal ein bis zwei Minuten) ..."
$project = Join-Path $repo 'src\Lotse.Web\Lotse.Web.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=false -o $appDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

# ---- 2. Data -----------------------------------------------------------------------------------------------------
$repoData = Join-Path $repo 'src\Lotse.Web\data'
if (-not (Test-Path (Join-Path $dataDir 'lotse.db')) -and (Test-Path (Join-Path $repoData 'lotse.db'))) {
    Write-Host "Uebernehme deinen bisherigen Lernstand aus $repoData ..."
    New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
    # The key ring (keys\) belongs with the database: without it a saved tutor key cannot be decrypted.
    # Audio cache and the like are rebuilt on demand, so a plain copy of the whole folder is right.
    Copy-Item -Path (Join-Path $repoData '*') -Destination $dataDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

# ---- 3. Launcher -------------------------------------------------------------------------------------------------
$launcherBody = @'
# Lotse-Starter, erzeugt von tools/install-windows.ps1. Startet den Server, falls noetig, und oeffnet den Browser.
param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'
$root = '__ROOT__'
$url = 'http://localhost:__PORT__'

function Test-Lotse {
    try { return (Invoke-WebRequest -Uri "$url/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 }
    catch { return $false }
}

if (-not (Test-Lotse)) {
    $env:Lotse__DataDirectory = Join-Path $root 'data'
    $env:Lotse__ContentDirectory = Join-Path $root 'app\content'
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    # Only set with -Tailscale. Safe because the server listens on localhost alone: the one proxy that can reach
    # it is tailscaled on this machine, so its X-Forwarded-Proto (https) may be trusted for cookies and passkeys.
    if ('__BEHINDPROXY__' -eq 'true') { $env:Lotse__Hosting__BehindProxy = 'true' }
    $exe = Join-Path $root 'app\Lotse.Web.exe'
    $log = Join-Path $root 'lotse.log'
    # A hidden PowerShell hosts the server so its console output (including password-reset links) lands in a log.
    $cmd = "& '$exe' --urls '$url' *>&1 | Out-File -FilePath '$log' -Encoding utf8"
    Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile', '-WindowStyle', 'Hidden', '-Command', $cmd -WindowStyle Hidden -WorkingDirectory (Join-Path $root 'app')
    $deadline = (Get-Date).AddSeconds(60)
    while (-not (Test-Lotse)) {
        if ((Get-Date) -gt $deadline) {
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show("Lotse startet nicht. Details stehen in $log", 'Lotse') | Out-Null
            exit 1
        }
        Start-Sleep -Milliseconds 500
    }
}
if (-not $NoBrowser) { Start-Process $url }
'@
$behindProxy = if ($Tailscale) { 'true' } else { 'false' }
$launcherBody.Replace('__ROOT__', $Root).Replace('__PORT__', [string]$Port).Replace('__BEHINDPROXY__', $behindProxy) | Set-Content -Path $launcher -Encoding UTF8

# ---- 4. Shortcuts ------------------------------------------------------------------------------------------------
if (-not $NoShortcuts) {
    New-Shortcut -Path $shortcuts[0] -Arguments '' -Description 'Lotse - Deutsch B2'
    New-Shortcut -Path $shortcuts[1] -Arguments '' -Description 'Lotse - Deutsch B2'
    if ($Autostart) { New-Shortcut -Path $shortcuts[2] -Arguments '-NoBrowser' -Description 'Lotse im Hintergrund starten' }
    elseif (Test-Path $shortcuts[2]) { Remove-Item $shortcuts[2] -Force }
}

Write-Host ''
Write-Host 'Fertig.'
Write-Host "  Starten:   Startmenue oder Desktop -> Lotse (oeffnet http://localhost:$Port)"
Write-Host "  Daten:     $dataDir"
Write-Host "  Protokoll: $(Join-Path $Root 'lotse.log')"
if ($Autostart) { Write-Host '  Autostart: Lotse laeuft ab der naechsten Anmeldung im Hintergrund.' }

# ---- 5. Tailscale (optional) -------------------------------------------------------------------------------------
if ($Tailscale) {
    $ts = Get-Command tailscale -ErrorAction SilentlyContinue
    if (-not $ts) { $ts = Get-Item (Join-Path $env:ProgramFiles 'Tailscale	ailscale.exe') -ErrorAction SilentlyContinue }
    if (-not $ts) {
        Write-Host ''
        Write-Warning 'Tailscale ist nicht installiert. https://tailscale.com/download installieren, anmelden und dieses Skript mit -Tailscale erneut ausfuehren.'
    }
    else {
        $tsExe = if ($ts.Source) { $ts.Source } else { $ts.FullName }
        # A running server keeps its old environment; restart it so the proxy setting takes effect.
        Get-Process -Name 'Lotse.Web' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $appDir 'Lotse.Web.exe') } | Stop-Process -Force
        & $tsExe serve --bg $Port
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Tailscale: Lotse ist auf deinen Geraeten im Tailnet erreichbar - die Adresse steht oben bzw. unter 'tailscale serve status'."
            Write-Host '             Der Rechner muss dafuer an sein und Lotse laufen (am einfachsten mit -Autostart).'
        }
        else { Write-Warning "tailscale serve ist fehlgeschlagen (Exit-Code $LASTEXITCODE). Ist Tailscale angemeldet? ('tailscale up')" }
    }
}
Write-Host '  Aktualisieren: dieses Skript nach "git pull" erneut ausfuehren. Entfernen: -Uninstall'
