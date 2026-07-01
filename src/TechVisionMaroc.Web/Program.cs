using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Serilog;
using System.Threading.RateLimiting;
using TechVisionMaroc.Data;
using TechVisionMaroc.Services;
using Microsoft.AspNetCore.HttpOverrides;

#pragma warning disable CS0105

// Force InvariantCulture so decimal inputs (type="number") with "." are parsed correctly
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/techvision-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// Services
builder.Services.AddControllersWithViews();

// ── Configuration des Forwarded Headers pour Ngrok ──────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // On ne fait confiance aux en-têtes X-Forwarded-* de n'importe quelle source QU'EN dev (tunnel ngrok).
    // En production, ne JAMAIS vider ces listes : déclarer l'IP réelle du reverse-proxy ci-dessous.
    if (builder.Environment.IsDevelopment())
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    }
    // else { options.KnownProxies.Add(System.Net.IPAddress.Parse("IP_DU_REVERSE_PROXY")); }
});

// ── Google OAuth (login social) ───────────────────────────────────
var googleId = builder.Configuration["Authentication:Google:ClientId"];
var googleSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleId) && !googleId.StartsWith("VOTRE_") && !string.IsNullOrWhiteSpace(googleSecret) && !googleSecret.StartsWith("VOTRE_"))
{
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.Google.GoogleDefaults.AuthenticationScheme;
    })
    .AddCookie(opts => { opts.LoginPath = "/Account/Connexion"; })
    .AddGoogle(opts =>
    {
        opts.ClientId = googleId;
        opts.ClientSecret = googleSecret;
        opts.CallbackPath = "/signin-google";
        opts.SaveTokens = true;
    });
}
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 10 * 1024 * 1024; // 10 Mo max
});
builder.Services.AddHttpContextAccessor();

// Rate limiting : 10 requêtes/min sur login & inscription, 60/min général
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter("global", o =>
    {
        o.PermitLimit = 60;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 5;
    });
    options.RejectionStatusCode = 429;

    // Limiteur GLOBAL appliqué à TOUTES les requêtes routées (les fichiers statiques sont servis avant
    // UseRateLimiter, donc non impactés). Partition par IP cliente : protège contre le flood / brute-force.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var cle = httpContext.Connection.RemoteIpAddress?.ToString() ?? "inconnu";
        return RateLimitPartition.GetFixedWindowLimiter(cle, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

// Session : Redis si disponible, sinon mémoire (développement)
var redisConn = builder.Configuration.GetConnectionString("Redis");
var redisDisponible = false;
if (!string.IsNullOrEmpty(redisConn))
{
    try
    {
        var cfg = StackExchange.Redis.ConfigurationOptions.Parse(redisConn);
        cfg.ConnectTimeout = 1000;
        cfg.AbortOnConnectFail = false;
        using var redis = StackExchange.Redis.ConnectionMultiplexer.Connect(cfg);
        redisDisponible = redis.IsConnected;
    }
    catch { }
}

if (redisDisponible)
{
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
    Log.Information("Session : Redis activé sur {Conn}", redisConn);
}
else
{
    builder.Services.AddDistributedMemoryCache();
    Log.Warning("Redis non disponible → session en mémoire (développement uniquement)");
}

// SameAsRequest : le cookie est marqué Secure automatiquement quand la requête est en HTTPS,
// et reste fonctionnel en HTTP. Indispensable derrière le proxy MonsterASP (sinon l'antiforgery
// plante avec « SecurePolicy = Always but the current request is not an SSL request »).
var politiqueCookieSecure = CookieSecurePolicy.SameAsRequest;

builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(30); // déconnexion après 30 min d'inactivité
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SecurePolicy = politiqueCookieSecure;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// Database
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default"),
        sql => sql.EnableRetryOnFailure(3)
                  .CommandTimeout(120)));   // 2 min au lieu de 30 sec par défaut

// Application services
builder.Services.AddScoped<IProduitService, ProduitService>();
builder.Services.AddScoped<IPanierService, PanierService>();
builder.Services.AddScoped<ICommandeService, CommandeService>();

// CORRECTION DU DOUBLON IIAService : On supprime la ligne vide AddScoped<IIAService, IAService>()
// On conserve uniquement la version nommée ci-dessous avec son HttpClient configuré.
builder.Services.AddScoped<IRecommandationCollaborativeService, RecommandationCollaborativeService>();
builder.Services.AddScoped<IPredictionStockService, PredictionStockService>();
builder.Services.AddScoped<IFactureService, FactureService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IPrixService, PrixService>();
builder.Services.AddScoped<IWishlistService, WishlistService>();
builder.Services.AddScoped<IFideliteService, FideliteService>();
builder.Services.AddScoped<ISuiviColisService, SuiviColisService>();
builder.Services.AddScoped<IFraudeService, FraudeService>();
builder.Services.AddScoped<ITrackingService, TrackingService>();
builder.Services.AddScoped<ISegmentationService, SegmentationService>();
builder.Services.AddHostedService<AlertePrixJob>();
builder.Services.AddHostedService<RecommandationJob>();
builder.Services.AddHostedService<SegmentationJob>();
builder.Services.AddHostedService<RelanceInactiviteJob>();

builder.Services.AddHttpClient<IChatbotService, ChatbotService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<IGenerationDescriptionService, GenerationDescriptionService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IRechercheSemantiqueService, RechercheSemantiqueService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(4);
});

// HTTP client pour service IA Python (timeout court → fallback automatique)
builder.Services.AddHttpClient<IIAService, IAService>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["IA:BaseUrl"] ?? "http://localhost:8000");
    c.Timeout = TimeSpan.FromSeconds(3);
});

// Anti-forgery
builder.Services.AddAntiforgery(o =>
{
    o.Cookie.SecurePolicy = politiqueCookieSecure;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
});

var app = builder.Build();

// ── CORRECTION NGROK 1 : Utilisation officielle du middleware de proxy en premier lieu ──
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    // CORRECTION NGROK 2 : En développement/test avec Ngrok HTTP, la redirection HTTPS intégrée de .NET casse le tunnel
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseSession();
app.UseMiddleware<TechVisionMaroc.Middleware.AdminSecuriteMiddleware>();
app.UseRouting();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Security headers (CORRECTION NGROK 3 : Ajout de l'origine Ngrok dans la directive form-action de la CSP)
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    ctx.Response.Headers["X-XSS-Protection"] = "0"; // obsolète : on s'appuie sur la CSP (recommandation OWASP)
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "camera=(self), microphone=(self), geolocation=()";
    ctx.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://js.stripe.com https://unpkg.com; " +
        "style-src  'self' 'unsafe-inline' https://cdn.jsdelivr.net https://fonts.googleapis.com https://unpkg.com; " +
        "font-src   'self' data: https://fonts.gstatic.com https://cdn.jsdelivr.net; " +
        "img-src    'self' data: https: blob:; " +
        "connect-src 'self' https://api.groq.com https://api.stripe.com https://*.tile.openstreetmap.org; " +
        "frame-src https://js.stripe.com https://www.google.com https://maps.google.com https://www.openstreetmap.org; " +
        "object-src 'none'; base-uri 'self'; frame-ancestors 'self'; form-action 'self' https://*.ngrok-free.dev https://accounts.google.com;";
    await next();
});

app.MapControllerRoute(
    name: "admin",
    pattern: "admin/{action=Dashboard}/{id?}",
    defaults: new { controller = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Corrections de schéma au démarrage
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // ── Provisionnement automatique (déploiement sur base vierge) ──────────────
    // Crée la base si absente, puis crée TOUTES les tables du modèle si elles
    // n'existent pas encore (cas d'un hébergeur qui fournit une base vide).
    // Sur la base locale existante : ne fait rien (comportement inchangé).
    try
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync()) await creator.CreateAsync();

        var produitsExiste = await db.Database
            .SqlQueryRaw<int>("SELECT CAST(COUNT(*) AS int) AS [Value] FROM sys.tables WHERE name = N'Produits'")
            .FirstAsync() > 0;
        if (!produitsExiste) await creator.CreateTablesAsync();
    }
    catch (Exception ex) { Log.Error(ex, "Provisionnement du schéma échoué"); }

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ImageUrl' AND Object_ID = Object_ID('Categories'))
            ALTER TABLE Categories ADD ImageUrl nvarchar(500) NULL;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('RecommandationsProduits', 'U') IS NULL
        BEGIN
            CREATE TABLE RecommandationsProduits (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                ProduitId           int NOT NULL,
                ProduitRecommandeId int NOT NULL,
                Score               float NOT NULL,
                CoOccurrences       int NOT NULL,
                DateCalcul          datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_RecoProd      FOREIGN KEY (ProduitId)           REFERENCES Produits(Id) ON DELETE CASCADE,
                CONSTRAINT FK_RecoProdReco  FOREIGN KEY (ProduitRecommandeId) REFERENCES Produits(Id)
            );
            CREATE UNIQUE INDEX IX_Reco_Paire ON RecommandationsProduits (ProduitId, ProduitRecommandeId);
            CREATE INDEX IX_Reco_Produit ON RecommandationsProduits (ProduitId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='DateNaissance')
            ALTER TABLE Utilisateurs ADD DateNaissance datetime2 NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='TentativesEchouees')
            ALTER TABLE Utilisateurs ADD TentativesEchouees int NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='BloqueJusqua')
            ALTER TABLE Utilisateurs ADD BloqueJusqua datetime2 NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='TotpSecret')
            ALTER TABLE Utilisateurs ADD TotpSecret nvarchar(64) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='TotpActive')
            ALTER TABLE Utilisateurs ADD TotpActive bit NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='ParrainId')
            ALTER TABLE Utilisateurs ADD ParrainId int NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='ParrainageRecompense')
            ALTER TABLE Utilisateurs ADD ParrainageRecompense bit NOT NULL DEFAULT 0;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Wishlists', 'U') IS NULL
        BEGIN
            CREATE TABLE Wishlists (
                Id              int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId   int NOT NULL,
                ProduitId       int NOT NULL,
                PrixReference   decimal(10,2) NOT NULL,
                AlertePrix      bit NOT NULL DEFAULT 1,
                DerniereAlerte  datetime2 NULL,
                DateAjout       datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_Wishlist_User    FOREIGN KEY (UtilisateurId) REFERENCES Utilisateurs(Id) ON DELETE CASCADE,
                CONSTRAINT FK_Wishlist_Produit FOREIGN KEY (ProduitId)     REFERENCES Produits(Id)     ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_Wishlist_User_Produit ON Wishlists (UtilisateurId, ProduitId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Utilisateurs') AND name='PointsFidelite')
            ALTER TABLE Utilisateurs ADD PointsFidelite int NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='PointsUtilises')
            ALTER TABLE Commandes ADD PointsUtilises int NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='ReductionFidelite')
            ALTER TABLE Commandes ADD ReductionFidelite decimal(10,2) NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='PointsGagnes')
            ALTER TABLE Commandes ADD PointsGagnes int NOT NULL DEFAULT 0;

        IF OBJECT_ID('TransactionsFidelite', 'U') IS NULL
        BEGIN
            CREATE TABLE TransactionsFidelite (
                Id              int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId   int NOT NULL,
                Type            int NOT NULL,
                Points          int NOT NULL,
                CommandeId      int NULL,
                Description     nvarchar(200) NULL,
                Date            datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateExpiration  datetime2 NULL,
                CONSTRAINT FK_Fid_User     FOREIGN KEY (UtilisateurId) REFERENCES Utilisateurs(Id) ON DELETE CASCADE,
                CONSTRAINT FK_Fid_Commande FOREIGN KEY (CommandeId)    REFERENCES Commandes(Id)
            );
            CREATE INDEX IX_Fid_User_Date ON TransactionsFidelite (UtilisateurId, Date DESC);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('EvenementsComportement', 'U') IS NULL
        BEGIN
            CREATE TABLE EvenementsComportement (
                Id              bigint IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId   int NULL,
                SessionId       nvarchar(80) NULL,
                Type            int NOT NULL,
                CibleType       nvarchar(40) NULL,
                CibleId         int NULL,
                Page            nvarchar(300) NULL,
                Referer         nvarchar(300) NULL,
                UserAgent       nvarchar(500) NULL,
                IpAdresse       nvarchar(45) NULL,
                Appareil        int NOT NULL DEFAULT 0,
                DureeMs         int NULL,
                Valeur          decimal(18,2) NULL,
                Etiquette       nvarchar(300) NULL,
                Metadonnees     nvarchar(1000) NULL,
                Date            datetime2 NOT NULL DEFAULT GETUTCDATE()
            );
            CREATE INDEX IX_Evt_Date ON EvenementsComportement (Date DESC);
            CREATE INDEX IX_Evt_Type_Date ON EvenementsComportement (Type, Date DESC);
            CREATE INDEX IX_Evt_User_Date ON EvenementsComportement (UtilisateurId, Date DESC);
            CREATE INDEX IX_Evt_Session ON EvenementsComportement (SessionId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('ScoresRFM', 'U') IS NULL
        BEGIN
            CREATE TABLE ScoresRFM (
                Id                       int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId            int NOT NULL,
                JoursDepuisDernierAchat  int NOT NULL,
                NombreCommandes          int NOT NULL,
                MontantTotal             decimal(18,2) NOT NULL,
                ScoreR                   int NOT NULL,
                ScoreF                   int NOT NULL,
                ScoreM                   int NOT NULL,
                Segment                  int NOT NULL,
                DateCalcul               datetime2 NOT NULL DEFAULT GETUTCDATE()
            );
            CREATE INDEX IX_RFM_User ON ScoresRFM (UtilisateurId);
            CREATE INDEX IX_RFM_Segment ON ScoresRFM (Segment);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('AbonnesNewsletter', 'U') IS NULL
        BEGIN
            CREATE TABLE AbonnesNewsletter (
                Id              int IDENTITY(1,1) PRIMARY KEY,
                Email           nvarchar(200) NOT NULL,
                DateInscription datetime2 NOT NULL DEFAULT GETUTCDATE(),
                EstActif        bit NOT NULL DEFAULT 1,
                Source          nvarchar(50) NULL
            );
            CREATE UNIQUE INDEX IX_Newsletter_Email ON AbonnesNewsletter (Email);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('AdressesClient', 'U') IS NULL
        BEGIN
            CREATE TABLE AdressesClient (
                Id              int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId   int NOT NULL,
                Libelle         nvarchar(80) NOT NULL,
                DestinataireNom nvarchar(100) NOT NULL,
                Telephone       nvarchar(20) NOT NULL,
                Adresse         nvarchar(250) NOT NULL,
                Ville           nvarchar(80) NOT NULL,
                CodePostal      nvarchar(20) NULL,
                Pays            nvarchar(80) NULL,
                Complement      nvarchar(300) NULL,
                ParDefaut        bit NOT NULL DEFAULT 0,
                DateCreation    datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_Adresse_User FOREIGN KEY (UtilisateurId) REFERENCES Utilisateurs(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_Adresse_User ON AdressesClient (UtilisateurId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Coupons', 'U') IS NULL
        BEGIN
            CREATE TABLE Coupons (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                Code                nvarchar(50) NOT NULL,
                Libelle             nvarchar(200) NOT NULL,
                Type                int NOT NULL DEFAULT 1,
                TypeReduction       int NOT NULL DEFAULT 0,
                Valeur              decimal(10,2) NOT NULL,
                MontantMinimum      decimal(10,2) NULL,
                ReductionMaximum    decimal(10,2) NULL,
                DateDebut           datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateFin             datetime2 NOT NULL,
                LimiteUtilisation   int NULL,
                UtilisationActuelle int NOT NULL DEFAULT 0,
                LimiteParClient     int NULL DEFAULT 1,
                EstActif            bit NOT NULL DEFAULT 1,
                UtilisateurId       int NULL,
                DateCreation        datetime2 NOT NULL DEFAULT GETUTCDATE()
            );
            CREATE UNIQUE INDEX IX_Coupon_Code ON Coupons (Code);
            CREATE INDEX IX_Coupon_User ON Coupons (UtilisateurId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('UtilisationsCoupon', 'U') IS NULL
        BEGIN
            CREATE TABLE UtilisationsCoupon (
                Id               int IDENTITY(1,1) PRIMARY KEY,
                CouponId         int NOT NULL,
                UtilisateurId    int NOT NULL,
                CommandeId       int NULL,
                MontantReduction decimal(10,2) NOT NULL,
                DateUtilisation  datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_UCoupon_Coupon FOREIGN KEY (CouponId) REFERENCES Coupons(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_UCoupon_User ON UtilisationsCoupon (UtilisateurId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Conversations', 'U') IS NULL
        BEGIN
            CREATE TABLE Conversations (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId      int NOT NULL,
                Sujet              nvarchar(200) NOT NULL,
                Statut             int NOT NULL DEFAULT 0,
                CommandeId         int NULL,
                ProduitId          int NULL,
                DateCreation       datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateDernierMessage datetime2 NOT NULL DEFAULT GETUTCDATE(),
                LuParClient        bit NOT NULL DEFAULT 1,
                LuParAdmin         bit NOT NULL DEFAULT 0,
                CONSTRAINT FK_Conv_User FOREIGN KEY (UtilisateurId) REFERENCES Utilisateurs(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_Conv_User ON Conversations (UtilisateurId, DateDernierMessage DESC);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Messages', 'U') IS NULL
        BEGIN
            CREATE TABLE Messages (
                Id             int IDENTITY(1,1) PRIMARY KEY,
                ConversationId int NOT NULL,
                Expediteur     int NOT NULL,
                Contenu        nvarchar(4000) NOT NULL,
                DateEnvoi      datetime2 NOT NULL DEFAULT GETUTCDATE(),
                CONSTRAINT FK_Msg_Conv FOREIGN KEY (ConversationId) REFERENCES Conversations(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_Msg_Conv ON Messages (ConversationId, DateEnvoi);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Abonnements', 'U') IS NULL
        BEGIN
            CREATE TABLE Abonnements (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId       int NOT NULL,
                ProduitId           int NOT NULL,
                Quantite            int NOT NULL DEFAULT 1,
                Frequence           int NOT NULL DEFAULT 1,
                Statut              int NOT NULL DEFAULT 0,
                RemiseAbonnement    decimal(5,2) NOT NULL DEFAULT 5,
                AdresseId           int NULL,
                DateCreation        datetime2 NOT NULL DEFAULT GETUTCDATE(),
                ProchaineLivraison  datetime2 NOT NULL,
                DerniereLivraison   datetime2 NULL,
                NbCommandesGenerees int NOT NULL DEFAULT 0,
                CONSTRAINT FK_Abo_User FOREIGN KEY (UtilisateurId) REFERENCES Utilisateurs(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_Abo_User ON Abonnements (UtilisateurId);
            CREATE INDEX IX_Abo_NextDelivery ON Abonnements (ProchaineLivraison, Statut);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('Devis', 'U') IS NULL
        BEGIN
            CREATE TABLE Devis (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                NumeroDevis         nvarchar(50) NOT NULL,
                UtilisateurId       int NULL,
                ContactNom          nvarchar(200) NOT NULL,
                ContactEmail        nvarchar(150) NOT NULL,
                ContactTelephone    nvarchar(20) NOT NULL,
                Entreprise          nvarchar(150) NULL,
                VilleLivraison      nvarchar(80) NULL,
                DemandeClient       nvarchar(2000) NOT NULL,
                Statut              int NOT NULL DEFAULT 0,
                ReponseAdmin        nvarchar(4000) NULL,
                MontantPropose      decimal(12,2) NULL,
                DelaiLivraisonJours int NULL,
                ValiditeJours       int NULL DEFAULT 30,
                DateCreation        datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateReponse         datetime2 NULL,
                DateDecision        datetime2 NULL,
                DateExpiration      datetime2 NULL,
                CommandeId          int NULL
            );
            CREATE UNIQUE INDEX IX_Devis_Numero ON Devis (NumeroDevis);
            CREATE INDEX IX_Devis_User ON Devis (UtilisateurId);
            CREATE INDEX IX_Devis_Statut ON Devis (Statut, DateCreation DESC);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('LignesDevis', 'U') IS NULL
        BEGIN
            CREATE TABLE LignesDevis (
                Id           int IDENTITY(1,1) PRIMARY KEY,
                DevisId      int NOT NULL,
                ProduitId    int NULL,
                Description  nvarchar(300) NOT NULL,
                Quantite     int NOT NULL DEFAULT 1,
                PrixUnitaire decimal(12,2) NULL,
                CONSTRAINT FK_LDevis_Devis FOREIGN KEY (DevisId) REFERENCES Devis(Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_LDevis_Devis ON LignesDevis (DevisId);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('EvaluationsFraude', 'U') IS NULL
        BEGIN
            CREATE TABLE EvaluationsFraude (
                Id                  int IDENTITY(1,1) PRIMARY KEY,
                CommandeId          int NOT NULL,
                UtilisateurId       int NOT NULL,
                Score               int NOT NULL,
                Niveau              int NOT NULL,
                Decision            int NOT NULL,
                Raisons             nvarchar(2000) NULL,
                IpAdresse           nvarchar(45) NULL,
                UserAgent           nvarchar(500) NULL,
                DateEvaluation      datetime2 NOT NULL DEFAULT GETUTCDATE(),
                DateRevision        datetime2 NULL,
                ReviseParId         int NULL,
                CommentaireRevision nvarchar(300) NULL,
                CONSTRAINT FK_Fraude_Commande FOREIGN KEY (CommandeId) REFERENCES Commandes(Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_Fraude_Commande ON EvaluationsFraude (CommandeId);
            CREATE INDEX IX_Fraude_Niveau ON EvaluationsFraude (Niveau, DateEvaluation DESC);
        END
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='Transporteur')
            ALTER TABLE Commandes ADD Transporteur int NOT NULL DEFAULT 0;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='NumeroSuivi')
            ALTER TABLE Commandes ADD NumeroSuivi nvarchar(80) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Commandes') AND name='DateExpedition')
            ALTER TABLE Commandes ADD DateExpedition datetime2 NULL;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Produits') AND name='TauxTVA')
            ALTER TABLE Produits ADD TauxTVA decimal(5,2) NOT NULL DEFAULT 20;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Produits') AND name='Image2')
            ALTER TABLE Produits ADD Image2 nvarchar(500) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Produits') AND name='Image3')
            ALTER TABLE Produits ADD Image3 nvarchar(500) NULL;
        IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID('Produits') AND name='Image4')
            ALTER TABLE Produits ADD Image4 nvarchar(500) NULL;
        """);

    await db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID('AuditLogs', 'U') IS NULL
        BEGIN
            CREATE TABLE AuditLogs (
                Id               int IDENTITY(1,1) PRIMARY KEY,
                UtilisateurId    int NULL,
                UtilisateurEmail nvarchar(200) NULL,
                Role             nvarchar(50) NULL,
                Action           nvarchar(60) NOT NULL,
                Cible            nvarchar(200) NULL,
                Details          nvarchar(500) NULL,
                IpAdresse        nvarchar(45) NULL,
                UserAgent        nvarchar(500) NULL,
                Date             datetime2 NOT NULL DEFAULT GETUTCDATE()
            );
            CREATE INDEX IX_Audit_Date ON AuditLogs (Date DESC);
            CREATE INDEX IX_Audit_User ON AuditLogs (UtilisateurId);
        END
        """);

    var corrections = new[]
    {
        ("Produits",     "Stock",              "int NOT NULL"),
        ("Produits",     "NombreAvis",         "int NOT NULL"),
        ("Produits",     "NombreVentes",       "int NOT NULL"),
        ("Avis",         "Note",               "int NOT NULL"),
        ("Commandes",    "Statut",             "int NOT NULL"),
        ("Commandes",    "MethodePaiement",    "int NOT NULL"),
        ("LignesCommande","Quantite",          "int NOT NULL"),
        ("LignesPanier", "Quantite",           "int NOT NULL"),
        ("Utilisateurs", "TentativesEchouees", "int NOT NULL"),
    };

    foreach (var (table, col, type) in corrections)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync($"""
                DECLARE @ck NVARCHAR(200);
                SELECT @ck = dc.name
                FROM sys.check_constraints dc
                JOIN sys.columns c ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
                WHERE OBJECT_NAME(dc.parent_object_id)='{table}' AND c.name='{col}';
                IF @ck IS NOT NULL
                    EXEC('ALTER TABLE [{table}] DROP CONSTRAINT [' + @ck + ']');

                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='{table}' AND COLUMN_NAME='{col}' AND DATA_TYPE='tinyint'
                )
                    ALTER TABLE [{table}] ALTER COLUMN [{col}] {type};
                """);
        }
        catch { }
    }

    // ── Seed automatique si la base est vide (déploiement) ─────────────────────
    // Réutilise les scripts App_Data/*.sql (mêmes que le bouton admin "Seed").
    try
    {
        var appData = Path.Combine(app.Environment.ContentRootPath, "App_Data");
        async Task ExecuterScript(string fichier)
        {
            var chemin = Path.Combine(appData, fichier);
            if (!File.Exists(chemin)) return;
            var sql = await File.ReadAllTextAsync(chemin);
            if (sql.Length > 0 && sql[0] == '﻿') sql = sql[1..]; // BOM
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        if (!await db.Produits.AnyAsync())
        {
            // Les produits référencent les catégories par NOM ; on garantit d'abord la hiérarchie
            // correcte (ResetCategories est idempotent : il réinitialise proprement les catégories).
            await ExecuterScript("ResetCategories.sql");
            await ExecuterScript("SeedProducts.sql");
            await ExecuterScript("UpdateImages.sql");
            Log.Information("Seed : catégories + produits créés.");
        }
        else if (!await db.Categories.AnyAsync())
        {
            await ExecuterScript("ResetCategories.sql");
            Log.Information("Seed : catégories créées.");
        }
    }
    catch (Exception ex) { Log.Error(ex, "Seed automatique échoué"); }

    // ── Création automatique d'un SuperAdmin si aucun n'existe (1er déploiement) ──
    // Évite le blocage « œuf-poule » : impossible de créer un admin sans être déjà admin.
    try
    {
        if (!await db.Utilisateurs.AnyAsync(u => u.Role == "SuperAdmin" || u.Role == "Admin"))
        {
            var emailAdmin = app.Configuration["Admin:SeedEmail"] ?? "mohammedkitouni86@gmail.com";
            var mdpAdmin   = app.Configuration["Admin:SeedMotDePasse"] ?? "Admin@SODIV2026";

            db.Utilisateurs.Add(new TechVisionMaroc.Models.Utilisateur
            {
                Prenom          = "Mohammed",
                Nom             = "Kitouni",
                Email           = emailAdmin,
                MotDePasseHash  = BCrypt.Net.BCrypt.HashPassword(mdpAdmin),
                Role            = "SuperAdmin",
                EstActif        = true,
                DateInscription = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            Log.Information("Seed : SuperAdmin créé ({Email}).", emailAdmin);
        }
    }
    catch (Exception ex) { Log.Error(ex, "Seed SuperAdmin échoué"); }
}

app.Run();