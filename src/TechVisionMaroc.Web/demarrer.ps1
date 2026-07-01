# ============================================================
#  Demarrage TechVisionMaroc (robuste)
#  1) Nettoie les noms de fichiers wwwroot (, et ; cassent la build)
#  2) Libere le port 5000 si Docker le squatte
#  3) Lance le site (http://localhost:5000)
# ============================================================
$ErrorActionPreference = 'SilentlyContinue'
Set-Location -LiteralPath $PSScriptRoot

Write-Host "[1/3] Nettoyage des noms de fichiers images..." -ForegroundColor Cyan
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'wwwroot') -Recurse -File |
    Where-Object { $_.Name -match '[,;]' } |
    ForEach-Object {
        $n = ($_.Name -replace '[,;]', '_')
        Rename-Item -LiteralPath $_.FullName -NewName $n
        Write-Host "  renomme: $($_.Name) -> $n"
    }

Write-Host "[2/3] Verification du port 5000..." -ForegroundColor Cyan
$c = Get-NetTCPConnection -LocalPort 5000 -State Listen
if ($c) {
    $procs = $c | Select-Object -ExpandProperty OwningProcess -Unique |
             ForEach-Object { Get-Process -Id $_ }
    if ($procs.ProcessName -match 'docker|wsl') {
        Write-Host "  Port 5000 occupe par Docker -> arret de Docker..." -ForegroundColor Yellow
        Get-Process -Name 'Docker Desktop','com.docker.backend','com.docker.service','dockerd','vpnkit','wslrelay','wslhost' |
            Stop-Process -Force
        wsl --shutdown 2>$null
        Start-Sleep -Seconds 3
        if (Get-NetTCPConnection -LocalPort 5000 -State Listen) {
            Write-Host "  ATTENTION: port 5000 toujours occupe." -ForegroundColor Red
        } else {
            Write-Host "  Port 5000 libere." -ForegroundColor Green
        }
    } else {
        Write-Host "  Port 5000 deja utilise par: $($procs.ProcessName -join ', ') (autre instance du site ?)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  Port 5000 libre." -ForegroundColor Green
}

Write-Host "[3/3] Lancement du site (http://localhost:5000)..." -ForegroundColor Cyan
$ErrorActionPreference = 'Continue'
dotnet run
