@echo off
rem Lotse starten - aus dem Release-Paket. Das kleine Fenster ist der Server: schliessen = Lotse beenden.
rem Daten liegen in %LOCALAPPDATA%\Lotse\data, getrennt vom Programm - ein neues Paket uebernimmt sie.
setlocal
set "PORT=5178"
set "URL=http://localhost:%PORT%"
set "Lotse__DataDirectory=%LOCALAPPDATA%\Lotse\data"
set "Lotse__ContentDirectory=%~dp0app\content"
set "ASPNETCORE_ENVIRONMENT=Production"

curl.exe -fs "%URL%/health" >nul 2>&1 && goto open
start "Lotse (Server - Fenster offen lassen)" /min "%~dp0app\Lotse.Web.exe" --urls "%URL%"

set /a tries=0
:wait
curl.exe -fs "%URL%/health" >nul 2>&1 && goto open
set /a tries+=1
if %tries% geq 60 (
    echo Lotse startet nicht. Details stehen im Server-Fenster.
    pause
    exit /b 1
)
timeout /t 1 /nobreak >nul
goto wait

:open
start "" "%URL%"
