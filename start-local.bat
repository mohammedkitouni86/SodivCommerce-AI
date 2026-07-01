@echo off
title SODIV Bureau - Demarrage Local
color 0A

echo ============================================
echo   SODIV Bureau - Demarrage en LOCAL
echo ============================================
echo.

:: ── 0. Détecter l'instance SQL Server ────────────────────────────────────
set SQL_SERVER=.\SQLEXPRESS
sqlcmd -S ".\SQLEXPRESS" -E -Q "SELECT 1" >nul 2>&1
if %errorlevel% neq 0 (
    sqlcmd -S "localhost" -E -Q "SELECT 1" >nul 2>&1
    if %errorlevel% equ 0 (set SQL_SERVER=localhost) else (
        echo [AVERTISSEMENT] SQL Server non accessible. Verifiez qu il est demarre.
    )
)
echo [OK] SQL Server detecte sur : %SQL_SERVER%

:: ── 1. Vérifier .NET ──────────────────────────────────────────────────────
echo [1/4] Verification .NET SDK...
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERREUR] .NET SDK non installe. Telecharger sur https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo [OK] .NET SDK detecte.

:: ── 2. Restaurer les packages NuGet ───────────────────────────────────────
echo.
echo [2/4] Restauration des packages NuGet...
cd /d "%~dp0src\TechVisionMaroc.Web"
dotnet restore
if %errorlevel% neq 0 (
    echo [ERREUR] Echec restauration packages.
    pause
    exit /b 1
)
echo [OK] Packages restaures.

:: ── 3. Lancer le service IA Python en arrière-plan ────────────────────────
echo.
echo [3/4] Demarrage du service IA Python (port 8000)...
cd /d "%~dp0src\TechVisionMaroc.AI"

:: Vérifier si l'environnement virtuel existe
if not exist ".venv" (
    echo Creation environnement virtuel Python...
    python -m venv .venv
    echo Installation des dependances Python...
    .venv\Scripts\pip install -r requirements.txt --quiet
    echo [OK] Dependances Python installees.
) else (
    echo [OK] Environnement Python existant detecte.
)

:: Lancer FastAPI dans une nouvelle fenêtre
start "TechVision IA (FastAPI)" cmd /k ".venv\Scripts\uvicorn main:app --host 0.0.0.0 --port 8000 --reload"
echo [OK] Service IA demarre sur http://localhost:8000
timeout /t 3 /nobreak >nul

:: ── 4. Lancer l'application ASP.NET Core ──────────────────────────────────
echo.
echo [4/4] Demarrage de l'application ASP.NET Core (port 5000)...
cd /d "%~dp0src\TechVisionMaroc.Web"

:: Lancer dans une nouvelle fenêtre
start "TechVision Web (ASP.NET)" cmd /k "dotnet run --launch-profile http"
echo [OK] Application web demarree sur http://localhost:5000

echo.
echo ============================================
echo   TOUT EST DEMARRE !
echo ============================================
echo.
echo   Site web  : http://localhost:5000
echo   API IA    : http://localhost:8000
echo   API docs  : http://localhost:8000/docs
echo.
echo   Admin     : admin@techvisionmaroc.ma / admin123
echo.
echo Appuyez sur une touche pour ouvrir le navigateur...
pause >nul
start http://localhost:5000
