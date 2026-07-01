@echo off
REM ============================================================
REM  Demarrage de Metabase (Business Intelligence) pour SODIV
REM ============================================================

echo.
echo === Recreation du conteneur SQL Server avec healthcheck corrige ===
docker-compose up -d --no-deps sqlserver

echo.
echo === Demarrage de Metabase (independant) ===
docker-compose up -d --no-deps metabase
if errorlevel 1 (
    echo.
    echo ERREUR : impossible de demarrer Metabase. Verifiez que Docker tourne.
    pause
    exit /b 1
)

echo.
echo === Application des vues analytiques ===
docker cp "database\vues_analytique.sql" techvision-sqlserver:/tmp/vues_analytique.sql >nul 2>&1
docker exec -i techvision-sqlserver /opt/mssql-tools18/bin/sqlcmd ^
    -S localhost -U sa -P "TechVision2025!" -C -No -d TechVisionMarocDB ^
    -i /tmp/vues_analytique.sql

echo.
echo ============================================================
echo  Metabase demarre (45-60 secondes la 1re fois)
echo.
echo  URL          : http://localhost:3030
echo  Setup initial dans l'UI :
echo    - Email     : admin@sodibureau.ma
echo    - Mot passe : (a definir)
echo    - Base SQL Server :
echo        Host    : sqlserver
echo        Port    : 1433
echo        DB      : TechVisionMarocDB
echo        User    : sa
echo        Pass    : TechVision2025!
echo ============================================================
echo.
pause
