# 📊 Metabase – Business Intelligence pour SODIV Bureau

## 1. Démarrage

```bat
start-metabase.bat
```

Ou manuellement :
```bash
docker-compose up -d metabase
```

Attendre 45–60 secondes la première fois (initialisation H2).

## 2. Configuration initiale

Ouvrir **http://localhost:3000** dans le navigateur.

### Étape 1 — Compte admin
- Prénom / Nom : *votre nom*
- Email : `admin@sodibureau.ma`
- Mot de passe : *choisir un mot de passe fort*

### Étape 2 — Connexion à la base
Sélectionner **SQL Server** et renseigner :

| Champ           | Valeur                |
| --------------- | --------------------- |
| Display name    | TechVisionMaroc       |
| Host            | `sqlserver`           |
| Port            | `1433`                |
| Database name   | `TechVisionMarocDB`   |
| Username        | `sa`                  |
| Password        | `TechVision2025!`     |

> ⚠️ Le host est `sqlserver` (nom du conteneur), **pas** `localhost`.

## 3. Vues SQL pré-créées

Le script `database/vues_analytique.sql` crée 7 vues prêtes à brancher :

| Vue                          | Description                                |
| ---------------------------- | ------------------------------------------ |
| `vAnalyseCAJournalier`       | CA, panier moyen, nb commandes par jour    |
| `vAnalyseTopProduits`        | Top produits (ventes, CA, note)            |
| `vAnalyseCategories`         | Performance par catégorie                  |
| `vAnalyseClients`            | Clients : LTV, fréquence, dernière cmd     |
| `vAlertesStock`              | Produits rupture / critique / faible       |
| `vAnalysePaiements`          | Répartition par méthode de paiement        |
| `vAnalyseStatutsCommandes`   | Entonnoir des statuts                      |

## 4. Dashboards suggérés

Créer dans Metabase (Browse → New → Dashboard) :

### Dashboard « Direction »
- KPI : CA du mois (vAnalyseCAJournalier)
- Courbe : CA 30 derniers jours
- Camembert : CA par catégorie
- Top 10 produits

### Dashboard « Opérations »
- Table : Alertes stock (vAlertesStock)
- Entonnoir : Statuts commandes
- Heatmap : commandes par jour/heure

### Dashboard « Marketing »
- Cohortes clients (par mois d'inscription)
- Top villes
- Taux de réachat
- Clients dormants (>60 jours)

## 5. Lien depuis l'admin

Un bouton **« Analytique BI »** a été ajouté en haut du Dashboard admin :
`/admin/dashboard` → ouvre `http://localhost:3000` dans un nouvel onglet.

## 6. Sauvegarde

Les dashboards Metabase sont stockés dans le volume Docker `metabase-data`.
Pour sauvegarder :
```bash
docker run --rm -v techvisionmaroc_metabase-data:/data -v %cd%:/backup ^
    alpine tar czf /backup/metabase-backup.tar.gz /data
```

## 7. Production

Pour la production, remplacer la base H2 par PostgreSQL dans `docker-compose.yml` :
```yaml
environment:
  - MB_DB_TYPE=postgres
  - MB_DB_HOST=postgres-metabase
  - MB_DB_PORT=5432
  - MB_DB_DBNAME=metabase
  - MB_DB_USER=metabase
  - MB_DB_PASS=...
```
