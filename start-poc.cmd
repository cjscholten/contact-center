@echo off
setlocal
rem Start de POC: backend + agent-pagina in een Windows Terminal-venster
rem met twee panelen (terugval: twee losse cmd-vensters).
rem Het publieke IP van de Asterisk-VM komt uit het eerste argument,
rem of anders uit vm-ip.txt naast dit script (bewust gitignored).

set "VM_IP=%~1"
if "%VM_IP%"=="" if exist "%~dp0vm-ip.txt" set /p VM_IP=<"%~dp0vm-ip.txt"
if "%VM_IP%"=="" (
    echo Geen VM-IP gevonden. Gebruik: %~nx0 IP-adres, of zet het IP in vm-ip.txt naast dit script.
    exit /b 1
)

echo Backend en agent-pagina starten voor Asterisk op %VM_IP% ...

where wt >nul 2>nul
if errorlevel 1 (
    start "CC backend" cmd /k dotnet run --project "%~dp0backend\src\ContactCenter.Api" -- --Ari:BaseUrl=http://%VM_IP%:8088/ari/
    start "CC agent-pagina" cmd /k npx --yes serve "%~dp0poc-agent" -l 3000
) else (
    wt --window new new-tab --suppressApplicationTitle --title "CC backend" -d "%~dp0." cmd /k dotnet run --project backend\src\ContactCenter.Api -- --Ari:BaseUrl=http://%VM_IP%:8088/ari/ ; split-pane -H --suppressApplicationTitle --title "CC agent-pagina" -d "%~dp0." cmd /k npx --yes serve poc-agent -l 3000
)

ping -n 4 127.0.0.1 >nul
start http://localhost:3000/?host=%VM_IP%
endlocal
