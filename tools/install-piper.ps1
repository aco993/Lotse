<#
.SYNOPSIS
    Installiert die lokale Sprachausgabe (Piper TTS) fuer Lotse.

.DESCRIPTION
    Piper ist ein neuronales Text-to-Speech, das komplett offline auf dieser Maschine laeuft:
    kein API-Schluessel, keine Netzwerkanfrage zur Laufzeit, nichts verlaesst den Rechner.
    Die Windows-Stimmen (Hedda/Katja/Stefan) sind aeltere konkatenative Stimmen und klingen
    deutlich robotischer - deshalb dieser Schritt.

    Geladen wird nach %LOCALAPPDATA%\Lotse (NICHT ins Repo, dafuer ist es zu gross):
      1. piper_windows_amd64.zip       (~21 MB, GitHub Release rhasspy/piper)
      2. Stimme de_DE-thorsten-medium  (~64 MB, HuggingFace rhasspy/piper-voices) - Standard- und Maennerstimme
      3. Stimme de_DE-kerstin-low      (~60 MB, dieselbe Quelle) - Frauenstimme fuer Sabine, Lena, Frau Kaya ...

    Die Frauenstimme gibt es nur als "low"-Modell und klingt etwas rauher als Thorsten; sie ist trotzdem
    deutlich natuerlicher als die Windows-Stimmen. Fehlt sie, spricht die App alle Rollen mit Thorsten.

    Das Skript ist idempotent: was schon da ist, wird nicht erneut geladen (-Force erzwingt es).

.PARAMETER Force
    Laedt auch dann neu, wenn die Dateien bereits vorhanden sind.

.PARAMETER Root
    Zielverzeichnis. Standard: %LOCALAPPDATA%\Lotse

.EXAMPLE
    pwsh -File tools/install-piper.ps1
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$Root = (Join-Path $env:LOCALAPPDATA 'Lotse')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # sonst bremst die Fortschrittsanzeige den Download massiv

$PiperRelease = 'https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip'
$VoiceHost = 'https://huggingface.co/rhasspy/piper-voices/resolve/main'

# Reihenfolge zaehlt: die erste Stimme ist die Standardstimme, mit der die Funktionsprobe laeuft.
$Voices = @(
    @{ Name = 'de_DE-thorsten-medium'; Pfad = 'de/de_DE/thorsten/medium'; Rolle = 'Standard- und Maennerstimme' }
    @{ Name = 'de_DE-kerstin-low'; Pfad = 'de/de_DE/kerstin/low'; Rolle = 'Frauenstimme' }
)

$piperDir = Join-Path $Root 'piper'
$voiceDir = Join-Path $Root 'voices'
$piperExe = Join-Path $piperDir 'piper.exe'
$voiceOnnx = Join-Path $voiceDir "$($Voices[0].Name).onnx"

function Get-File {
    param([string]$Url, [string]$Ziel, [string]$Label)
    if ((Test-Path $Ziel) -and -not $Force) {
        $mb = [math]::Round((Get-Item $Ziel).Length / 1MB, 1)
        Write-Host "  [vorhanden] $Label ($mb MB)"
        return
    }
    Write-Host "  [lade]      $Label ..."
    $tmp = "$Ziel.download"
    Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
    Move-Item -Path $tmp -Destination $Ziel -Force
    $mb = [math]::Round((Get-Item $Ziel).Length / 1MB, 1)
    Write-Host "  [fertig]    $Label ($mb MB)"
}

Write-Host "Lotse - lokale Sprachausgabe einrichten"
Write-Host "Ziel: $Root"
Write-Host ""

New-Item -ItemType Directory -Force -Path $Root, $voiceDir | Out-Null

# ---- 1. Piper-Binary -------------------------------------------------------------------------------
if ((Test-Path $piperExe) -and -not $Force) {
    Write-Host "  [vorhanden] piper.exe"
}
else {
    $zip = Join-Path $Root 'piper_windows_amd64.zip'
    Get-File -Url $PiperRelease -Ziel $zip -Label 'piper_windows_amd64.zip'
    Write-Host "  [entpacke]  piper_windows_amd64.zip ..."
    # Das Archiv bringt seinen eigenen Ordner "piper" mit, deshalb nach $Root entpacken.
    if (Test-Path $piperDir) { Remove-Item $piperDir -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $Root -Force
    Remove-Item $zip -Force
    if (-not (Test-Path $piperExe)) { throw "Nach dem Entpacken fehlt $piperExe - Archivaufbau unerwartet." }
    Write-Host "  [fertig]    piper.exe"
}

# ---- 2. Stimmen ------------------------------------------------------------------------------------
foreach ($v in $Voices) {
    Write-Host "  --- $($v.Name) ($($v.Rolle))"
    Get-File -Url "$VoiceHost/$($v.Pfad)/$($v.Name).onnx"      -Ziel (Join-Path $voiceDir "$($v.Name).onnx")      -Label "$($v.Name).onnx"
    Get-File -Url "$VoiceHost/$($v.Pfad)/$($v.Name).onnx.json" -Ziel (Join-Path $voiceDir "$($v.Name).onnx.json") -Label "$($v.Name).onnx.json"
}

# ---- 3. Funktionsprobe -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Funktionsprobe ..."
# Nicht in $env:TEMP: dessen 8.3-Kurzform enthaelt eine Tilde (…\LOKALE~1\Temp), und Remove-Item deutet
# "~" als Home-Verzeichnis - der Loeschversuch scheitert dann mit "Objekt im Pfad nicht vorhanden".
# Zusaetzlich ueberall -LiteralPath, damit gar keine Pfadinterpretation stattfindet.
$probe = Join-Path $Root 'probe.wav'
if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
# Piper schreibt seine Statuszeilen nach stderr. In Windows PowerShell 5.1 macht die Engine daraus
# ErrorRecords (NativeCommandError) und $ErrorActionPreference='Stop' bricht ab, obwohl der Aufruf
# erfolgreich war - deshalb hier bewusst nicht abbrechen und stattdessen die erzeugte Datei pruefen.
$vorher = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
'Guten Tag, hier spricht die lokale Sprachausgabe.' | & $piperExe --model $voiceOnnx --output_file $probe 2>$null
$ErrorActionPreference = $vorher
if (-not (Test-Path -LiteralPath $probe) -or (Get-Item -LiteralPath $probe).Length -lt 1000) {
    throw "Piper hat keine hoerbare WAV erzeugt ($probe). Installation unvollstaendig."
}
$kb = [math]::Round((Get-Item -LiteralPath $probe).Length / 1KB, 1)
Remove-Item -LiteralPath $probe -Force
Write-Host "  OK - Probe-WAV erzeugt ($kb KB)."
Write-Host ""
Write-Host "Fertig. Lotse findet das alles automatisch:"
Write-Host "  piper.exe : $piperExe"
foreach ($v in $Voices) {
    Write-Host ("  Stimme    : {0}  ({1})" -f (Join-Path $voiceDir "$($v.Name).onnx"), $v.Rolle)
}
Write-Host ""
Write-Host "Andere Pfade? In appsettings.json setzen:"
Write-Host "  Lotse:Tts:PiperPath / Lotse:Tts:VoicePath / Lotse:Tts:VoiceFemalePath"
