@echo off
title SODIV Bureau - Logs Docker
color 07

cd /d "%~dp0"
echo Choix des logs a afficher :
echo.
echo [1] Application Web (ASP.NET)
echo [2] Service IA (Python)
echo [3] SQL Server
echo [4] Redis
echo [5] Tous les services
echo [6] Quitter
echo.
set /p choix="Votre choix (1-6) : "

if "%choix%"=="1" docker-compose logs -f --tail=100 web
if "%choix%"=="2" docker-compose logs -f --tail=100 ia
if "%choix%"=="3" docker-compose logs -f --tail=100 sqlserver
if "%choix%"=="4" docker-compose logs -f --tail=100 redis
if "%choix%"=="5" docker-compose logs -f --tail=50
if "%choix%"=="6" exit
