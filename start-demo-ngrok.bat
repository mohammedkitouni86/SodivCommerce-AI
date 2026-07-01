@echo off
title SODIV Bureau - Demo en ligne (App + ngrok)
color 0A
chcp 65001 >nul

echo ============================================
echo   SODIV Bureau - DEMO EN LIGNE (ngrok)
echo ============================================
echo.

:: ── 1. Liberer le port 5000 (anciennes instances) ──
echo [1/3] Nettoyage du port 5000...
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5000 " 2^>nul') do taskkill /pid %%p /f >nul 2>&1
taskkill /f /im ngrok.exe >nul 2>&1
timeout /t 2 /nobreak >nul

:: ── 2. Lancer l'application web ──
echo [2/3] Demarrage de l'application (port 5000)...
cd /d "%~dp0src\TechVisionMaroc.Web"
start "SODIV - Application Web" cmd /k "set ASPNETCORE_ENVIRONMENT=Development&& dotnet run --launch-profile http"

echo     Attente du demarrage (35 s)...
timeout /t 35 /nobreak >nul

:: ── 3. Lancer le tunnel ngrok ──
echo [3/3] Demarrage du tunnel ngrok...
start "SODIV - Tunnel ngrok" cmd /k "ngrok http 5000"

timeout /t 6 /nobreak >nul

echo.
echo ============================================
echo   TOUT EST EN LIGNE !
echo ============================================
echo.
echo   Site local  : http://localhost:5000
echo   Lien public : https://wildcard-drippy-banker.ngrok-free.dev
echo   Tableau ngrok : http://localhost:4040
echo.
echo   NE FERMEZ PAS les 2 fenetres (App + ngrok)
echo   pendant la demonstration.
echo.
echo Appuyez sur une touche pour ouvrir le lien public...
pause >nul
start https://wildcard-drippy-banker.ngrok-free.dev
