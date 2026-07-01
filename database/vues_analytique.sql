-- ============================================================================
--  Vues analytiques pour Metabase / Business Intelligence
--  TechVisionMaroc / SODIV Bureau
-- ============================================================================
--  À exécuter UNE FOIS sur la base TechVisionMarocDB après installation.
--  Chaque vue est recréée (DROP+CREATE) pour idempotence.
-- ============================================================================

USE TechVisionMarocDB;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 1. CHIFFRE D'AFFAIRES PAR JOUR
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalyseCAJournalier', 'V') IS NOT NULL DROP VIEW vAnalyseCAJournalier;
GO
CREATE VIEW vAnalyseCAJournalier AS
SELECT
    CAST(c.DateCommande AS DATE)                  AS Jour,
    COUNT(DISTINCT c.Id)                          AS NombreCommandes,
    COUNT(DISTINCT c.UtilisateurId)               AS NombreClients,
    SUM(c.Total)                                  AS ChiffreAffaires,
    AVG(c.Total)                                  AS PanierMoyen,
    SUM(CASE WHEN c.Statut = 5 THEN c.Total ELSE 0 END) AS CAAnnule
FROM Commandes c
GROUP BY CAST(c.DateCommande AS DATE);
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 2. TOP PRODUITS (par ventes / CA)
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalyseTopProduits', 'V') IS NOT NULL DROP VIEW vAnalyseTopProduits;
GO
CREATE VIEW vAnalyseTopProduits AS
SELECT
    p.Id                              AS ProduitId,
    p.Nom                             AS Produit,
    p.Marque,
    cat.Nom                           AS Categorie,
    p.Prix                            AS PrixActuel,
    p.Stock,
    p.NoteMoyenne,
    p.NombreAvis,
    COUNT(DISTINCT lc.CommandeId)     AS NombreCommandes,
    ISNULL(SUM(lc.Quantite), 0)       AS UnitesVendues,
    ISNULL(SUM(lc.Quantite * lc.PrixUnitaire), 0) AS ChiffreAffaires
FROM Produits p
LEFT JOIN Categories cat       ON cat.Id = p.CategorieId
LEFT JOIN LignesCommande lc    ON lc.ProduitId = p.Id
LEFT JOIN Commandes co         ON co.Id = lc.CommandeId AND co.Statut <> 5
GROUP BY p.Id, p.Nom, p.Marque, cat.Nom, p.Prix, p.Stock, p.NoteMoyenne, p.NombreAvis;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 3. PERFORMANCES PAR CATÉGORIE
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalyseCategories', 'V') IS NOT NULL DROP VIEW vAnalyseCategories;
GO
CREATE VIEW vAnalyseCategories AS
SELECT
    cat.Id                            AS CategorieId,
    cat.Nom                           AS Categorie,
    COUNT(DISTINCT p.Id)              AS NombreProduits,
    SUM(CASE WHEN p.EstActif = 1 THEN 1 ELSE 0 END) AS ProduitsActifs,
    SUM(p.Stock)                      AS StockTotal,
    AVG(p.Prix)                       AS PrixMoyen,
    AVG(p.NoteMoyenne)                AS NoteMoyenne,
    ISNULL(SUM(lc.Quantite), 0)       AS UnitesVendues,
    ISNULL(SUM(lc.Quantite * lc.PrixUnitaire), 0) AS ChiffreAffaires
FROM Categories cat
LEFT JOIN Produits p           ON p.CategorieId = cat.Id
LEFT JOIN LignesCommande lc    ON lc.ProduitId = p.Id
LEFT JOIN Commandes co         ON co.Id = lc.CommandeId AND co.Statut <> 5
GROUP BY cat.Id, cat.Nom;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 4. CLIENTS (cohorte, valeur vie, fréquence)
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalyseClients', 'V') IS NOT NULL DROP VIEW vAnalyseClients;
GO
CREATE VIEW vAnalyseClients AS
SELECT
    u.Id                              AS ClientId,
    u.Prenom + ' ' + u.Nom            AS Nom,
    u.Email,
    u.Ville,
    CAST(u.DateInscription AS DATE)   AS DateInscription,
    DATEDIFF(DAY, u.DateInscription, GETDATE()) AS JoursDepuisInscription,
    COUNT(DISTINCT c.Id)              AS NombreCommandes,
    ISNULL(SUM(c.Total), 0)           AS CAClient,
    ISNULL(AVG(c.Total), 0)           AS PanierMoyen,
    MAX(c.DateCommande)               AS DerniereCommande,
    DATEDIFF(DAY, MAX(c.DateCommande), GETDATE()) AS JoursDepuisDerniereCommande
FROM Utilisateurs u
LEFT JOIN Commandes c ON c.UtilisateurId = u.Id AND c.Statut <> 5
WHERE u.EstActif = 1
GROUP BY u.Id, u.Prenom, u.Nom, u.Email, u.Ville, u.DateInscription;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 5. STOCK ALERTES (rupture / faible)
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAlertesStock', 'V') IS NOT NULL DROP VIEW vAlertesStock;
GO
CREATE VIEW vAlertesStock AS
SELECT
    p.Id,
    p.Nom                             AS Produit,
    p.Marque,
    cat.Nom                           AS Categorie,
    p.Stock,
    p.Prix,
    CASE
        WHEN p.Stock = 0  THEN 'RUPTURE'
        WHEN p.Stock < 5  THEN 'CRITIQUE'
        WHEN p.Stock < 15 THEN 'FAIBLE'
        ELSE 'OK'
    END                               AS NiveauStock,
    ISNULL(v.UnitesVendues30j, 0)     AS VentesDernierMois
FROM Produits p
LEFT JOIN Categories cat ON cat.Id = p.CategorieId
OUTER APPLY (
    SELECT SUM(lc.Quantite) AS UnitesVendues30j
    FROM LignesCommande lc
    JOIN Commandes co ON co.Id = lc.CommandeId
    WHERE lc.ProduitId = p.Id
      AND co.DateCommande >= DATEADD(DAY, -30, GETDATE())
      AND co.Statut <> 5
) v
WHERE p.EstActif = 1 AND p.Stock < 20;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 6. MÉTHODES DE PAIEMENT
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalysePaiements', 'V') IS NOT NULL DROP VIEW vAnalysePaiements;
GO
CREATE VIEW vAnalysePaiements AS
SELECT
    CASE c.MethodePaiement
        WHEN 0 THEN 'Carte bancaire'
        WHEN 1 THEN 'PayPal'
        WHEN 2 THEN 'Stripe'
        WHEN 3 THEN 'CMI'
        WHEN 4 THEN 'Espèces à la livraison'
        WHEN 5 THEN 'Virement'
        ELSE 'Autre'
    END                               AS Methode,
    COUNT(*)                          AS NombreTransactions,
    SUM(c.Total)                      AS Montant,
    AVG(c.Total)                      AS PanierMoyen
FROM Commandes c
WHERE c.Statut <> 5
GROUP BY c.MethodePaiement;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 7. STATUTS COMMANDES (entonnoir)
-- ────────────────────────────────────────────────────────────────────────────
IF OBJECT_ID('vAnalyseStatutsCommandes', 'V') IS NOT NULL DROP VIEW vAnalyseStatutsCommandes;
GO
CREATE VIEW vAnalyseStatutsCommandes AS
SELECT
    CASE c.Statut
        WHEN 0 THEN '1. En attente'
        WHEN 1 THEN '2. Confirmée'
        WHEN 2 THEN '3. En préparation'
        WHEN 3 THEN '4. Expédiée'
        WHEN 4 THEN '5. Livrée'
        WHEN 5 THEN '6. Annulée'
        ELSE 'Inconnu'
    END                               AS Statut,
    COUNT(*)                          AS Nombre,
    SUM(c.Total)                      AS Montant
FROM Commandes c
GROUP BY c.Statut;
GO

-- ────────────────────────────────────────────────────────────────────────────
-- 8. RECHERCHE TENDANCES (top mots-clés recherchés - nécessite tracking)
-- ────────────────────────────────────────────────────────────────────────────
-- À alimenter via un futur log de recherches.

PRINT '✅ Vues analytiques créées avec succès';
GO
