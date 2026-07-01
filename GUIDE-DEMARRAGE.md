# Guide de Démarrage – SODIV Bureau

---

## MÉTHODE 1 : Exécution en LOCAL

### Prérequis

| Outil          | Version minimale | Lien de téléchargement |
|----------------|-----------------|------------------------|
| .NET SDK       | 8.0             | https://dotnet.microsoft.com/download |
| SQL Server     | 2022 Express    | https://www.microsoft.com/sql-server |
| Python         | 3.11            | https://www.python.org/downloads |
| SSMS (optionnel) | –            | https://aka.ms/ssmsfullsetup |

---

### Étapes détaillées

#### 1. Créer la base de données

**Option A – Via SSMS :**
1. Ouvrez SQL Server Management Studio
2. Connectez-vous à `localhost` avec l'authentification Windows
3. Ouvrez le fichier `database\schema.sql`
4. Cliquez sur `Exécuter` (F5)

**Option B – Via ligne de commande :**
```cmd
sqlcmd -S localhost -E -i "database\schema.sql"
```

#### 2. Configurer la connexion

Ouvrez `src\TechVisionMaroc.Web\appsettings.json` et vérifiez :

```json
"ConnectionStrings": {
  "Default": "Server=localhost;Database=TechVisionMarocDB;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

> Si vous utilisez un login SQL (pas Windows) :
> `"Server=localhost;Database=TechVisionMarocDB;User Id=sa;Password=VotreMotDePasse;TrustServerCertificate=True;"`

#### 3. Installer les dépendances Python

```cmd
cd src\TechVisionMaroc.AI
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt
```

#### 4. Démarrer le service IA (terminal 1)

```cmd
cd src\TechVisionMaroc.AI
.venv\Scripts\activate
uvicorn main:app --host 0.0.0.0 --port 8000 --reload
```
✅ Vérifier : http://localhost:8000/health → `{"status":"ok"}`
✅ Documentation API : http://localhost:8000/docs

#### 5. Démarrer l'application web (terminal 2)

```cmd
cd src\TechVisionMaroc.Web
dotnet restore
dotnet run --launch-profile http
```
✅ Le site est accessible sur : **http://localhost:5000**

#### OU – Démarrage automatique en un clic

Double-cliquez sur **`start-local.bat`** — tout se lance automatiquement.

---

### Comptes de test

| Rôle    | Email                          | Mot de passe |
|---------|--------------------------------|--------------|
| Admin   | admin@techvisionmaroc.ma       | admin123     |

---

## MÉTHODE 2 : Exécution avec DOCKER

### Prérequis

| Outil          | Lien |
|----------------|------|
| Docker Desktop | https://www.docker.com/products/docker-desktop |

> Docker Desktop inclut Docker Engine + Docker Compose.
> Après installation, redémarrez Windows et attendez que Docker démarre (icône dans la barre des tâches).

---

### Étapes détaillées

#### 1. Vérifier que Docker fonctionne

```cmd
docker --version
docker-compose --version
docker info
```

#### 2. Construire et lancer tous les services

```cmd
cd "C:\Users\huawei\Desktop\SODIV BUREAU\TechVisionMaroc"
docker-compose up --build -d
```

Cela lance automatiquement :
- **SQL Server 2022** sur le port 1433
- **Redis 7** sur le port 6379
- **Service IA Python** sur le port 8000
- **Application ASP.NET** sur le port 5000
- **Nginx** sur les ports 80 et 443

#### 3. Attendre le démarrage (30-60 secondes)

```cmd
docker-compose ps
```

Tous les services doivent afficher **Up**.

#### 4. Initialiser la base de données

```cmd
docker exec -it techvision-sqlserver /opt/mssql-tools/bin/sqlcmd ^
  -S localhost -U sa -P "TechVision2025!" ^
  -Q "CREATE DATABASE TechVisionMarocDB"

docker exec -i techvision-sqlserver /opt/mssql-tools/bin/sqlcmd ^
  -S localhost -U sa -P "TechVision2025!" ^
  -d TechVisionMarocDB < database\schema.sql
```

#### 5. Accéder au site

✅ **http://localhost** (port 80 via Nginx)
✅ **http://localhost:8000/docs** (API IA)

#### OU – Démarrage automatique Docker en un clic

Double-cliquez sur **`start-docker.bat`**

---

### Commandes Docker utiles

```cmd
# Voir l'état des conteneurs
docker-compose ps

# Voir les logs d'un service
docker-compose logs -f web
docker-compose logs -f ia
docker-compose logs -f sqlserver

# Redémarrer un service
docker-compose restart web

# Arrêter tout (données conservées)
docker-compose down

# Arrêter et supprimer les données
docker-compose down -v

# Rebuild d'un seul service
docker-compose up --build -d web
```

---

### Résolution de problèmes courants

#### Problème : Port déjà utilisé
```cmd
netstat -ano | findstr :5000
taskkill /pid NUMERO_PID /f
```

#### Problème : Erreur connexion SQL Server (local)
- Vérifier que SQL Server est démarré : `services.msc` → SQL Server (MSSQLSERVER)
- Activer TCP/IP : SQL Server Configuration Manager → Protocoles → TCP/IP → Activer

#### Problème : Docker "cannot connect to daemon"
- Ouvrir Docker Desktop et attendre qu'il soit complètement démarré
- Redémarrer Docker Desktop si nécessaire

#### Problème : Service IA Python ne répond pas
- L'application fonctionne sans IA (fallback automatique sur les produits de la même catégorie)
- Vérifier les logs : `docker-compose logs ia`

#### Problème : `dotnet: command not found`
- Redémarrer le terminal après installation de .NET SDK
- Vérifier : `echo %PATH%` doit contenir le chemin .NET

---

### Architecture des services

```
Navigateur
    │
    ▼
Nginx :80/:443
    │
    ▼
ASP.NET Core :5000 ──────► FastAPI IA :8000
    │
    ├──► SQL Server :1433
    └──► Redis :6379
```
