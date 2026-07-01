@echo off
REM ============================================================
REM  Configure SQLEXPRESS : TCP/IP sur port 1433
REM  DOIT etre execute en tant qu'ADMINISTRATEUR
REM ============================================================

net session >nul 2>&1
if errorlevel 1 (
    echo.
    echo ============================================================
    echo  ERREUR : Ce script doit etre execute en tant qu'ADMIN
    echo  Clic droit sur le .bat - "Executer en tant qu'administrateur"
    echo ============================================================
    pause
    exit /b 1
)

echo === Activation TCP/IP sur SQLEXPRESS ===
powershell -NoProfile -Command "$tcp='HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQLServer\SuperSocketNetLib\Tcp'; Set-ItemProperty -Path $tcp -Name 'Enabled' -Value 1; Set-ItemProperty -Path \"$tcp\IPAll\" -Name 'TcpPort' -Value '1433'; Set-ItemProperty -Path \"$tcp\IPAll\" -Name 'TcpDynamicPorts' -Value ''; Write-Host 'Registre mis a jour.'"

echo.
echo === Redemarrage du service SQL Server (SQLEXPRESS) ===
net stop MSSQL$SQLEXPRESS
net start MSSQL$SQLEXPRESS

echo.
echo === Verification : port 1433 ===
powershell -NoProfile -Command "$p = Get-Process sqlservr -ErrorAction SilentlyContinue; if ($p) { foreach ($x in $p) { Get-NetTCPConnection -OwningProcess $x.Id -State Listen -ErrorAction SilentlyContinue | Format-Table LocalAddress, LocalPort -AutoSize } }"

echo.
echo Termine. Fermez cette fenetre.
pause
