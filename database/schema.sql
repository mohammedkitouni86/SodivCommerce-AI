-- =============================================
-- SODIV Bureau SARL – Schéma SQL Server
-- =============================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TechVisionMarocDB')
    CREATE DATABASE TechVisionMarocDB COLLATE French_CI_AS;
GO

USE TechVisionMarocDB;
GO

-- ── Categories ──────────────────────────────────────────────────────────────

CREATE TABLE Categories (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    Nom         NVARCHAR(100) NOT NULL,
    Slug        NVARCHAR(100) NOT NULL,
    IconeClass  NVARCHAR(50)  NOT NULL DEFAULT 'fa-box',
    EstActive   BIT           NOT NULL DEFAULT 1,
    ParentId    INT REFERENCES Categories(Id)
);
GO

-- ── Utilisateurs ─────────────────────────────────────────────────────────────

CREATE TABLE Utilisateurs (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    Prenom          NVARCHAR(100) NOT NULL,
    Nom             NVARCHAR(100) NOT NULL,
    Email           NVARCHAR(200) NOT NULL UNIQUE,
    MotDePasseHash  NVARCHAR(500) NOT NULL,
    Telephone       NVARCHAR(20)  NOT NULL DEFAULT '',
    Adresse         NVARCHAR(300) NOT NULL DEFAULT '',
    Ville           NVARCHAR(100) NOT NULL DEFAULT '',
    Role            NVARCHAR(20)  NOT NULL DEFAULT 'Client',
    EstActif        BIT           NOT NULL DEFAULT 1,
    DateInscription DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    DerniereConnexion DATETIME2   NULL
);
GO

CREATE INDEX IX_Utilisateurs_Email ON Utilisateurs(Email);
GO

-- ── Produits ─────────────────────────────────────────────────────────────────

CREATE TABLE Produits (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Nom          NVARCHAR(200)    NOT NULL,
    Description  NVARCHAR(MAX)    NOT NULL DEFAULT '',
    Prix         DECIMAL(10,2)    NOT NULL,
    PrixPromo    DECIMAL(10,2)    NULL,
    Stock        INT              NOT NULL DEFAULT 0,
    ImageUrl     NVARCHAR(500)    NOT NULL DEFAULT '',
    Marque       NVARCHAR(100)    NOT NULL DEFAULT '',
    Reference    NVARCHAR(50)     NOT NULL UNIQUE,
    EstActif     BIT              NOT NULL DEFAULT 1,
    EstVedette   BIT              NOT NULL DEFAULT 0,
    NoteMoyenne  FLOAT            NOT NULL DEFAULT 0,
    NombreAvis   INT              NOT NULL DEFAULT 0,
    NombreVentes INT              NOT NULL DEFAULT 0,
    DateCreation DATETIME2        NOT NULL DEFAULT GETUTCDATE(),
    CategorieId  INT              NOT NULL REFERENCES Categories(Id)
);
GO

CREATE INDEX IX_Produits_Nom ON Produits(Nom);
CREATE INDEX IX_Produits_CategorieId ON Produits(CategorieId);
CREATE INDEX IX_Produits_EstActif ON Produits(EstActif);

-- Index unique non-clustered requis comme clé Full-Text Search
CREATE UNIQUE NONCLUSTERED INDEX UIX_Produits_Id ON Produits(Id);
GO

-- Full-text search (nécessite le service SQL Full-Text installé)
IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'FTCatalog')
    CREATE FULLTEXT CATALOG FTCatalog AS DEFAULT;
GO

IF NOT EXISTS (SELECT * FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID('Produits'))
    CREATE FULLTEXT INDEX ON Produits(Nom, Description, Marque) KEY INDEX UIX_Produits_Id ON FTCatalog;
GO

-- ── Commandes ────────────────────────────────────────────────────────────────

CREATE TABLE Commandes (
    Id                INT IDENTITY(1,1) PRIMARY KEY,
    NumeroCommande    NVARCHAR(30)  NOT NULL UNIQUE,
    UtilisateurId     INT           NOT NULL REFERENCES Utilisateurs(Id),
    SousTotal         DECIMAL(10,2) NOT NULL,
    FraisLivraison    DECIMAL(10,2) NOT NULL DEFAULT 0,
    Total             DECIMAL(10,2) NOT NULL,
    Statut            INT           NOT NULL DEFAULT 0,
    MethodePaiement   INT           NOT NULL DEFAULT 0,
    StripePaymentIntentId NVARCHAR(200) NULL,
    AdresseLivraison  NVARCHAR(300) NOT NULL,
    VilleLivraison    NVARCHAR(100) NOT NULL,
    TelephoneLivraison NVARCHAR(20) NOT NULL,
    Notes             NVARCHAR(500) NULL,
    DateCommande      DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
    DateLivraison     DATETIME2     NULL
);
GO

CREATE INDEX IX_Commandes_UtilisateurId ON Commandes(UtilisateurId);
CREATE INDEX IX_Commandes_Statut ON Commandes(Statut);
CREATE INDEX IX_Commandes_DateCommande ON Commandes(DateCommande DESC);
GO

-- ── Lignes commande ──────────────────────────────────────────────────────────

CREATE TABLE LignesCommande (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    CommandeId   INT           NOT NULL REFERENCES Commandes(Id) ON DELETE CASCADE,
    ProduitId    INT           NOT NULL REFERENCES Produits(Id),
    Quantite     INT           NOT NULL,
    PrixUnitaire DECIMAL(10,2) NOT NULL
);
GO

-- ── Paniers ──────────────────────────────────────────────────────────────────

CREATE TABLE Paniers (
    Id              INT IDENTITY(1,1) PRIMARY KEY,
    UtilisateurId   INT           NULL REFERENCES Utilisateurs(Id),
    SessionId       NVARCHAR(200) NULL,
    DateMiseAJour   DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);
GO

CREATE TABLE LignesPanier (
    Id        INT IDENTITY(1,1) PRIMARY KEY,
    PanierId  INT NOT NULL REFERENCES Paniers(Id) ON DELETE CASCADE,
    ProduitId INT NOT NULL REFERENCES Produits(Id),
    Quantite  INT NOT NULL DEFAULT 1
);
GO

-- ── Avis ─────────────────────────────────────────────────────────────────────

CREATE TABLE Avis (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    ProduitId        INT           NOT NULL REFERENCES Produits(Id),
    UtilisateurId    INT           NOT NULL REFERENCES Utilisateurs(Id),
    Note             TINYINT       NOT NULL CHECK (Note BETWEEN 1 AND 5),
    Commentaire      NVARCHAR(1000) NOT NULL DEFAULT '',
    AnalyseSentiment NVARCHAR(20)  NOT NULL DEFAULT 'Neutre',
    EstValide        BIT           NOT NULL DEFAULT 0,
    DateCreation     DATETIME2     NOT NULL DEFAULT GETUTCDATE()
);
GO

-- ── Données de démarrage ─────────────────────────────────────────────────────

INSERT INTO Categories (Nom, Slug, IconeClass) VALUES
    ('Ordinateurs & Laptops',     'ordinateurs',  'fa-laptop'),
    ('Imprimantes & Scanners',    'imprimantes',  'fa-print'),
    ('Réseaux & Connectivité',    'reseaux',      'fa-network-wired'),
    ('Fournitures de Bureau',     'fournitures',  'fa-briefcase'),
    ('Accessoires Informatiques', 'accessoires',  'fa-keyboard'),
    ('Stockage & Sauvegarde',     'stockage',     'fa-hard-drive');

-- Compte admin
INSERT INTO Utilisateurs (Prenom, Nom, Email, MotDePasseHash, Role)
VALUES ('Admin', 'TechVision', 'admin@techvisionmaroc.ma',
        '$2a$12$LQv3c1yqBWVHxkd0LHAkCOYz6TtxMQJqhN8/ew0Uuu.9qe3lBj5jO', -- admin123
        'Admin');

-- Produits d'exemple
INSERT INTO Produits (Nom, Description, Prix, Stock, Marque, Reference, EstVedette, CategorieId) VALUES
    ('HP EliteBook 840 G10', 'Laptop professionnel Intel Core i7-1365U, 16GB RAM, 512GB SSD, Écran 14" FHD IPS', 12500.00, 15, 'HP', 'TVM-LAPTOP-001', 1, 1),
    ('Dell Latitude 5540', 'Laptop entreprise Intel Core i5-1345U, 8GB RAM, 256GB SSD, Écran 15.6"', 9800.00, 20, 'Dell', 'TVM-LAPTOP-002', 1, 1),
    ('Lenovo ThinkPad X1 Carbon', 'Ultrabook premium 1.12kg, Intel Core i7, 16GB, 1TB SSD, Écran 14" WUXGA', 18900.00, 8, 'Lenovo', 'TVM-LAPTOP-003', 1, 1),
    ('HP LaserJet Pro M404dn', 'Imprimante laser monochrome réseau, 38 ppm, recto-verso automatique', 3200.00, 25, 'HP', 'TVM-PRINT-001', 1, 2),
    ('Canon PIXMA G3420', 'Imprimante multifonction couleur jet d''encre, WiFi, réservoir rechargeable', 1890.00, 30, 'Canon', 'TVM-PRINT-002', 0, 2),
    ('Cisco RV340 Router', 'Routeur VPN professionnel, 4 ports WAN, Gigabit, double bande WiFi', 4500.00, 10, 'Cisco', 'TVM-NET-001', 0, 3),
    ('TP-Link TL-SG1024', 'Switch non administrable 24 ports Gigabit, rack 19"', 1200.00, 20, 'TP-Link', 'TVM-NET-002', 0, 3),
    ('WD My Passport 2TB', 'Disque dur externe USB 3.0, protection par mot de passe, compatible Windows/Mac', 650.00, 50, 'Western Digital', 'TVM-STCK-001', 0, 6),
    ('Samsung 870 EVO 1TB SSD', 'SSD interne SATA 2.5", 560 Mo/s lecture, 530 Mo/s écriture', 1100.00, 35, 'Samsung', 'TVM-STCK-002', 1, 6),
    ('Logitech MX Keys', 'Clavier sans fil premium rétroéclairé, compatible multi-appareils, Windows/Mac', 890.00, 40, 'Logitech', 'TVM-ACC-001', 1, 5);

GO

PRINT 'Base de données TechVisionMarocDB créée avec succès !';
GO
