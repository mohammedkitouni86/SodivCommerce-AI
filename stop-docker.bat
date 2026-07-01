@echo off
title SODIV Bureau - Arret Docker
color 0C

echo Arret de tous les conteneurs SODIV Bureau...
cd /d "%~dp0"
docker-compose down
echo.
echo [OK] Tous les conteneurs sont arretes.
echo Les donnees sont conservees dans les volumes Docker.
echo.
echo Pour supprimer aussi les donnees (ATTENTION - irreversible) :
echo   docker-compose down -v
echo.
pause
