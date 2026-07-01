@echo off
title SODIV Bureau - Arret
color 0C

echo Arret de SODIV Bureau...

:: Fermer les fenêtres par titre
taskkill /fi "WindowTitle eq TechVision IA (FastAPI)*" /f >nul 2>&1
taskkill /fi "WindowTitle eq TechVision Web (ASP.NET)*" /f >nul 2>&1

:: Tuer les processus sur les ports
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":5000 " 2^>nul') do taskkill /pid %%p /f >nul 2>&1
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":8000 " 2^>nul') do taskkill /pid %%p /f >nul 2>&1

echo [OK] Tous les services sont arretes.
timeout /t 2 /nobreak >nul
