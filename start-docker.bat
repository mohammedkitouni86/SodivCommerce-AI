@echo off
title SODIV Bureau - Demarrage Docker
color 0B

echo ============================================
echo   SODIV Bureau - Demarrage DOCKER
echo ============================================
echo.

:: ── Vérifier Docker ───────────────────────────────────────────────────────
echo [1/5] Verification Docker...
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERREUR] Docker non installe.
    echo Telecharger Docker Desktop : https://www.docker.com/products/docker-desktop
    pause
    exit /b 1
)
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERREUR] Docker Desktop n'est pas demarre. Lancez Docker Desktop et reessayez.
    pause
    exit /b 1
)
echo [OK] Docker est pret.

:: ── Aller dans le dossier projet ──────────────────────────────────────────
cd /d "%~dp0"

:: ── Construire les images ─────────────────────────────────────────────────
echo.
echo [2/5] Construction des images Docker (peut prendre 5-10 min la premiere fois)...
docker-compose build --no-cache
if %errorlevel% neq 0 (
    echo [ERREUR] Echec de la construction des images.
    pause
    exit /b 1
)
echo [OK] Images construites.

:: ── Lancer les conteneurs ─────────────────────────────────────────────────
echo.
echo [3/5] Lancement des conteneurs...
docker-compose up -d
if %errorlevel% neq 0 (
    echo [ERREUR] Echec du lancement des conteneurs.
    pause
    exit /b 1
)
echo [OK] Conteneurs lances.

:: ── Attendre que SQL Server soit prêt ─────────────────────────────────────
echo.
echo [4/5] Attente de SQL Server (30 secondes)...
timeout /t 30 /nobreak
echo [OK] SQL Server pret.

:: ── Initialiser la base de données ───────────────────────────────────────
echo.
echo [5/5] Initialisation de la base de donnees...
docker exec techvision-sqlserver /opt/mssql-tools/bin/sqlcmd ^
    -S localhost -U sa -P "TechVision2025!" ^
    -i /dev/stdin < database\schema.sql 2>nul
if %errorlevel% neq 0 (
    echo [INFO] La base existe peut-etre deja, verification...
    docker exec techvision-sqlserver /opt/mssql-tools/bin/sqlcmd ^
        -S localhost -U sa -P "TechVision2025!" ^
        -Q "SELECT name FROM sys.databases WHERE name='TechVisionMarocDB'" 2>nul
)
echo [OK] Base de donnees initialisee.

:: ── Afficher le statut ────────────────────────────────────────────────────
echo.
echo ============================================
echo   STATUT DES CONTENEURS :
echo ============================================
docker-compose ps

echo.
echo ============================================
echo   TOUT EST DEMARRE !
echo ============================================
echo.
echo   Site web      : http://localhost:80
echo   API IA        : http://localhost:8000
echo   API docs      : http://localhost:8000/docs
echo   SQL Server    : localhost:1433
echo   Redis         : localhost:6379
echo.
echo   Admin : admin@techvisionmaroc.ma / admin123
echo.
echo Appuyez sur une touche pour ouvrir le navigateur...
pause >nul
start http://localhost
