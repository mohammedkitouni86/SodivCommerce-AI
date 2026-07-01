@echo off
REM Lance TechVisionMaroc de facon robuste (nettoyage noms de fichiers + liberation port 5000 + dotnet run).
REM Toute la logique est dans demarrer.ps1.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0demarrer.ps1"
pause
