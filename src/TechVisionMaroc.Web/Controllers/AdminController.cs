using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IProduitService _produitService;
    private readonly ICommandeService _commandeService;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;

    public AdminController(AppDbContext db, IProduitService produitService, ICommandeService commandeService, IWebHostEnvironment env, IConfiguration config)
    {
        _db = db;
        _produitService = produitService;
        _commandeService = commandeService;
        _env = env;
        _config = config;
    }

    // ── Helpers de rôle ───────────────────────────────────────────────────────

    private string RoleSession => HttpContext.Session.GetString("UtilisateurRole") ?? "";
    private bool EstSuperAdmin  => RoleSession == "SuperAdmin";
    private bool EstAdminOuPlus => RoleSession is "Admin" or "SuperAdmin";
    private string SecretPrefix => "/" + (_config["Admin:RoutePrefix"] ?? "admin").TrimStart('/');

    public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        base.OnActionExecuting(context);
        ViewBag.AdminBase    = SecretPrefix;
        ViewBag.EstSuperAdmin = EstSuperAdmin;
    }

    // ── PIN ───────────────────────────────────────────────────────────────────

    [HttpGet("pin")]
    public IActionResult Pin(string? returnUrl)
    {
        ViewBag.ReturnUrl    = returnUrl ?? SecretPrefix;
        ViewBag.PinActionUrl = SecretPrefix + "/pin";
        return View();
    }

    [HttpPost("pin")]
    [ValidateAntiForgeryToken]
    public IActionResult Pin(string code, string? returnUrl)
    {
        var pinAttendu = _config["Admin:PinCode"] ?? "";
        if (code == pinAttendu)
        {
            HttpContext.Session.SetString("AdminPinOk", "1");
            // Anti open-redirect : n'accepter qu'une URL locale, sinon repli sur le préfixe admin.
            var dest = (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) ? returnUrl : SecretPrefix;
            return Redirect(dest);
        }
        ViewBag.ReturnUrl    = returnUrl ?? SecretPrefix;
        ViewBag.PinActionUrl = SecretPrefix + "/pin";
        ViewBag.Erreur = "Code PIN incorrect.";
        return View();
    }

    // ── Vérification accès ────────────────────────────────────────────────────

    private IActionResult? VerifierAdmin()
    {
        if (!EstAdminOuPlus)
            return RedirectToAction("Connexion", "Account",
                new { returnUrl = HttpContext.Request.Path.Value });

        var pin = _config["Admin:PinCode"] ?? "";
        if (!string.IsNullOrEmpty(pin) && HttpContext.Session.GetString("AdminPinOk") != "1")
            return Redirect(SecretPrefix + "/pin?returnUrl=" + Uri.EscapeDataString(SecretPrefix));

        return null;
    }

    private IActionResult? VerifierSuperAdmin()
    {
        var check = VerifierAdmin();
        if (check != null) return check;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Accès réservé au Super Administrateur.";
            return RedirectToAction(nameof(Dashboard));
        }
        return null;
    }

    // ── Upload image ──────────────────────────────────────────────────────────

    // .svg retiré volontairement : un SVG uploadé peut contenir du JavaScript (XSS stocké, servi en same-origin).
    // Pour une image vectorielle, utiliser le champ URL (fichier hébergé ailleurs).
    private static readonly string[] _extensionsAutorisees = [".jpg", ".jpeg", ".png", ".gif", ".webp"];

    private async Task<string?> SauvegarderImage(IFormFile? fichier)
    {
        if (fichier == null || fichier.Length == 0) return null;

        var ext = Path.GetExtension(fichier.FileName).ToLowerInvariant();
        if (!_extensionsAutorisees.Contains(ext)) return null;

        var dossier = Path.Combine(_env.WebRootPath, "uploads", "produits");
        Directory.CreateDirectory(dossier);

        var nomFichier = $"{Guid.NewGuid():N}{ext}";
        var chemin = Path.Combine(dossier, nomFichier);

        using var stream = new FileStream(chemin, FileMode.Create);
        await fichier.CopyToAsync(stream);

        return $"/uploads/produits/{nomFichier}";
    }

    // Normalise une URL d'image saisie : null si vide, sinon valeur nettoyée.
    private static string? NettoyerUrl(string? url)
        => string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    // ── Dashboard ─────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Dashboard(DateTime? debut, DateTime? fin)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        // Période par défaut : 30 derniers jours, sinon paramètres du form
        var dateFin    = (fin    ?? DateTime.UtcNow.Date).AddDays(1).AddSeconds(-1);
        var dateDebut  = (debut  ?? DateTime.UtcNow.Date.AddDays(-30));
        ViewBag.PeriodeDebut = dateDebut;
        ViewBag.PeriodeFin   = (fin ?? DateTime.UtcNow.Date);

        // Requête de base : commandes dans la période
        var baseQ = _db.Commandes.Where(c => c.DateCommande >= dateDebut && c.DateCommande <= dateFin);
        var validesQ = baseQ.Where(c => c.Statut != StatutCommande.Annulee && c.Statut != StatutCommande.Remboursee);

        // KPIs principaux
        ViewBag.VentesTotal    = await validesQ.SumAsync(c => (decimal?)c.Total) ?? 0;
        ViewBag.CommandesTotal = await baseQ.CountAsync();
        ViewBag.PanierMoyen    = ViewBag.CommandesTotal > 0
                                  ? (decimal)ViewBag.VentesTotal / (int)ViewBag.CommandesTotal
                                  : 0m;
        ViewBag.NouveauxClients = await _db.Utilisateurs.CountAsync(u => u.DateInscription >= dateDebut && u.DateInscription <= dateFin);
        ViewBag.StockFaible     = await _db.Produits.CountAsync(p => p.Stock < 10 && p.EstActif);

        // Répartition statuts (camembert)
        var statuts = await baseQ
            .GroupBy(c => c.Statut)
            .Select(g => new { Statut = g.Key, Nb = g.Count(), CA = g.Sum(c => c.Total) })
            .ToListAsync();
        ViewBag.RepartitionStatuts = statuts;

        // Top 5 produits vendus (par quantité dans la période)
        ViewBag.TopProduits = await _db.LignesCommande
            .Where(l => l.Commande.DateCommande >= dateDebut && l.Commande.DateCommande <= dateFin
                     && l.Commande.Statut != StatutCommande.Annulee)
            .GroupBy(l => new { l.ProduitId, l.Produit.Nom, l.Produit.ImageUrl })
            .Select(g => new {
                ProduitId = g.Key.ProduitId,
                Nom       = g.Key.Nom,
                ImageUrl  = g.Key.ImageUrl,
                QteVendue = g.Sum(l => l.Quantite),
                CA        = g.Sum(l => l.Quantite * l.PrixUnitaire)
            })
            .OrderByDescending(x => x.QteVendue)
            .Take(5)
            .ToListAsync();

        // Top 5 clients (par CA dans la période)
        ViewBag.TopClients = await baseQ
            .Where(c => c.Statut != StatutCommande.Annulee)
            .GroupBy(c => new { c.UtilisateurId, c.Utilisateur.Prenom, c.Utilisateur.Nom, c.Utilisateur.Email })
            .Select(g => new {
                UserId  = g.Key.UtilisateurId,
                Nom     = (g.Key.Prenom ?? "") + " " + (g.Key.Nom ?? ""),
                Email   = g.Key.Email,
                NbCmd   = g.Count(),
                CA      = g.Sum(c => c.Total)
            })
            .OrderByDescending(x => x.CA)
            .Take(5)
            .ToListAsync();

        // Dernières commandes
        ViewBag.DernieresCommandes = await baseQ
            .Include(c => c.Utilisateur)
            .OrderByDescending(c => c.DateCommande)
            .Take(10).ToListAsync();

        // Courbe : ventes agrégées par jour/semaine/mois selon la durée
        var nbJours = (dateFin - dateDebut).Days + 1;
        string format;
        if      (nbJours <=  31) format = "yyyy-MM-dd";   // jour
        else if (nbJours <= 180) format = "yyyy-ww";      // semaine
        else                     format = "yyyy-MM";     // mois

        var brut = await validesQ
            .Select(c => new { c.DateCommande, c.Total })
            .ToListAsync();
        var courbe = brut
            .GroupBy(c => format switch
            {
                "yyyy-MM-dd" => c.DateCommande.ToString("yyyy-MM-dd"),
                "yyyy-ww"    => $"{c.DateCommande.Year}-S{System.Globalization.ISOWeek.GetWeekOfYear(c.DateCommande):D2}",
                _            => c.DateCommande.ToString("yyyy-MM")
            })
            .Select(g => new { Label = g.Key, Total = g.Sum(x => x.Total) })
            .OrderBy(x => x.Label)
            .ToList();
        ViewBag.VentesCourbe = courbe;
        ViewBag.CourbeGranularite = format == "yyyy-MM-dd" ? "jour" : (format == "yyyy-ww" ? "semaine" : "mois");

        // Répartition par méthode de paiement
        ViewBag.RepartitionPaiement = await validesQ
            .GroupBy(c => c.MethodePaiement)
            .Select(g => new { Methode = g.Key, Nb = g.Count(), CA = g.Sum(c => c.Total) })
            .ToListAsync();

        // Répartition par ville
        ViewBag.RepartitionVilles = await validesQ
            .GroupBy(c => c.VilleLivraison)
            .Select(g => new { Ville = g.Key, Nb = g.Count(), CA = g.Sum(c => c.Total) })
            .OrderByDescending(x => x.CA)
            .Take(8)
            .ToListAsync();

        return View();
    }

    // ── 2FA (Google Authenticator) ────────────────────────────────────────────

    [HttpGet("2fa")]
    public async Task<IActionResult> ConfigurerTotp()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var userId = HttpContext.Session.GetInt32("UtilisateurId");
        var user = await _db.Utilisateurs.FindAsync(userId);
        if (user == null) return RedirectToAction("Connexion", "Account");

        if (string.IsNullOrEmpty(user.TotpSecret))
        {
            user.TotpSecret = TotpService.GenererSecret();
            await _db.SaveChangesAsync();
        }
        ViewBag.Secret = user.TotpSecret;
        ViewBag.OtpAuthUri = TotpService.GenererUriQrCode(user.TotpSecret, user.Email, "SODIV Bureau Admin");
        ViewBag.Active = user.TotpActive;
        return View();
    }

    [HttpPost("2fa/activer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActiverTotp(string code, [FromServices] IAuditService audit)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var userId = HttpContext.Session.GetInt32("UtilisateurId");
        var user = await _db.Utilisateurs.FindAsync(userId);
        if (user == null || string.IsNullOrEmpty(user.TotpSecret))
            return RedirectToAction(nameof(ConfigurerTotp));

        if (!TotpService.VerifierCode(user.TotpSecret, code ?? ""))
        {
            TempData["Error"] = "Code invalide. Réessayez.";
            return RedirectToAction(nameof(ConfigurerTotp));
        }
        user.TotpActive = true;
        await _db.SaveChangesAsync();
        await audit.LogAsync("ACTIVATE_2FA", $"Utilisateur #{user.Id}");
        TempData["Success"] = "✅ Authentification à deux facteurs activée.";
        return RedirectToAction(nameof(ConfigurerTotp));
    }

    [HttpPost("2fa/desactiver")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DesactiverTotp([FromServices] IAuditService audit)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var userId = HttpContext.Session.GetInt32("UtilisateurId");
        var user = await _db.Utilisateurs.FindAsync(userId);
        if (user == null) return RedirectToAction(nameof(ConfigurerTotp));
        user.TotpActive = false;
        user.TotpSecret = null;
        await _db.SaveChangesAsync();
        await audit.LogAsync("DEACTIVATE_2FA", $"Utilisateur #{user.Id}");
        TempData["Success"] = "2FA désactivée.";
        return RedirectToAction(nameof(ConfigurerTotp));
    }

    // ── Audit log ────────────────────────────────────────────────────────────

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(int page = 1, string? action = null)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var q = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrEmpty(action)) q = q.Where(a => a.Action == action);
        ViewBag.Total   = await q.CountAsync();
        ViewBag.Page    = page;
        ViewBag.Action  = action;
        ViewBag.Actions = await _db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        return View(await q.OrderByDescending(a => a.Date).Skip((page - 1) * 50).Take(50).ToListAsync());
    }

    // ── Prévisions stock (IA / analyse comportement) ─────────────────────────

    [HttpGet("previsions-stock")]
    public async Task<IActionResult> PrevisionsStock([FromServices] IPredictionStockService svc, string? niveau = null)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var previsions = await svc.CalculerToutAsync(90);

        if (!string.IsNullOrEmpty(niveau) && Enum.TryParse<NiveauAlerte>(niveau, true, out var n))
            previsions = previsions.Where(p => p.Niveau == n).ToList();

        ViewBag.NiveauFiltre = niveau;
        ViewBag.Compteurs = new
        {
            Critique  = previsions.Count(p => p.Niveau == NiveauAlerte.Critique),
            Attention = previsions.Count(p => p.Niveau == NiveauAlerte.Attention),
            Ok        = previsions.Count(p => p.Niveau == NiveauAlerte.Ok),
            Surstock  = previsions.Count(p => p.Niveau == NiveauAlerte.Surstock),
            SansVente = previsions.Count(p => p.Niveau == NiveauAlerte.SansVente)
        };
        return View(previsions);
    }

    // ── Produits ──────────────────────────────────────────────────────────────

    [HttpGet("produits")]
    public async Task<IActionResult> Produits(string? q, int page = 1, bool stockFaible = false)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var query = _db.Produits.Include(p => p.Categorie).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))  query = query.Where(p => p.Nom.Contains(q));
        if (stockFaible)                     query = query.Where(p => p.Stock < 10 && p.EstActif);
        ViewBag.Total       = await query.CountAsync();
        ViewBag.Page        = page;
        ViewBag.StockFaible = stockFaible;
        return View(await query.OrderBy(p => p.Stock).Skip((page - 1) * 20).Take(20).ToListAsync());
    }

    private Task<List<Categorie>> ChargerCategoriesAsync() =>
        _db.Categories
            .Where(c => c.EstActive)
            .Include(c => c.SousCategories.Where(s => s.EstActive))
            .ToListAsync();

    [HttpGet("produits/creer")]
    public async Task<IActionResult> CreerProduit()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        ViewBag.Categories = await ChargerCategoriesAsync();
        return View(new Produit());
    }

    [HttpPost("produits/creer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreerProduit(
        string nom, string description, string marque, int categorieId, int? sousCategorieId,
        decimal prix, decimal? prixPromo, int stock, string? imageUrl,
        IFormFile? imageFile, bool estActif, bool estVedette, decimal tauxTVA = 20m)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        var categorieFinale = sousCategorieId.GetValueOrDefault() > 0 ? sousCategorieId!.Value : categorieId;

        if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(marque) || categorieFinale == 0)
        {
            TempData["Error"] = "Tous les champs obligatoires doivent être remplis.";
            ViewBag.Categories = await ChargerCategoriesAsync();
            return View(new Produit { Nom = nom, Description = description, Marque = marque, CategorieId = categorieFinale, Prix = prix, Stock = stock, ImageUrl = imageUrl ?? "" });
        }

        var urlFinale = await SauvegarderImage(imageFile) ?? imageUrl ?? string.Empty;

        var produit = new Produit
        {
            Nom          = nom,
            Description  = description,
            Marque       = marque,
            CategorieId  = categorieFinale,
            Prix         = prix,
            PrixPromo    = prixPromo,
            TauxTVA      = tauxTVA,
            Stock        = stock,
            ImageUrl     = urlFinale,
            EstActif     = estActif,
            EstVedette   = estVedette,
            DateCreation = DateTime.UtcNow
        };

        await _produitService.CreerAsync(produit);
        TempData["Success"] = $"Produit \"{produit.Nom}\" créé avec succès (réf: {produit.Reference}).";
        return RedirectToAction(nameof(Produits));
    }

    [HttpGet("produits/modifier/{id}")]
    public async Task<IActionResult> ModifierProduit(int id)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var produit = await _db.Produits.FindAsync(id);
        if (produit == null) return NotFound();

        var cats = await ChargerCategoriesAsync();
        // Garantir que la catégorie actuelle (et son parent) soient dans la liste,
        // même si elles sont inactives, pour qu'elles s'affichent pré-sélectionnées.
        if (produit.CategorieId > 0 && cats.All(c => c.Id != produit.CategorieId))
        {
            var courante = await _db.Categories.FindAsync(produit.CategorieId);
            if (courante != null)
            {
                if (courante.ParentId.HasValue)
                {
                    var parent = cats.FirstOrDefault(c => c.Id == courante.ParentId.Value)
                                 ?? await _db.Categories.FindAsync(courante.ParentId.Value);
                    if (parent != null)
                    {
                        if (cats.All(c => c.Id != parent.Id)) cats.Add(parent);
                        // attacher la sous-catégorie courante à son parent pour le menu déroulant
                        if (parent.SousCategories.All(s => s.Id != courante.Id))
                            parent.SousCategories.Add(courante);
                    }
                }
                else
                {
                    cats.Add(courante);
                }
            }
        }
        ViewBag.Categories = cats;
        return View(produit);
    }

    [HttpPost("produits/modifier/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierProduit(int id,
        string nom, string description, string marque, int categorieId, int? sousCategorieId,
        decimal prix, decimal? prixPromo, int stock, string? imageUrl,
        IFormFile? imageFile,
        string? imageUrl2, string? imageUrl3, string? imageUrl4,
        IFormFile? imageFile2, IFormFile? imageFile3, IFormFile? imageFile4,
        bool estActif = false, bool estVedette = false, decimal tauxTVA = 20m)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        var existant = await _db.Produits.FindAsync(id);
        if (existant == null) return NotFound();

        // Catégorie / sous-catégorie : on ne change QUE si une sélection valide est faite.
        // Si rien n'est choisi (0), on conserve la catégorie actuelle du produit.
        if (sousCategorieId.GetValueOrDefault() > 0)
            existant.CategorieId = sousCategorieId!.Value;
        else if (categorieId > 0)
            existant.CategorieId = categorieId;
        // sinon : existant.CategorieId reste inchangé

        existant.Nom         = nom;
        existant.Description = description;
        existant.Marque      = marque;
        existant.Prix        = prix;
        existant.PrixPromo   = prixPromo;
        existant.TauxTVA     = tauxTVA;
        existant.Stock       = stock;
        existant.EstActif    = estActif;
        existant.EstVedette  = estVedette;

        // Photo 1 (principale) : fichier prioritaire, sinon URL
        var nouvelleUrl = await SauvegarderImage(imageFile);
        if (nouvelleUrl != null)    existant.ImageUrl = nouvelleUrl;
        else if (imageUrl != null)  existant.ImageUrl = imageUrl;

        // Photos 2, 3, 4 : fichier prioritaire, sinon URL (vide => effacée)
        existant.Image2 = await SauvegarderImage(imageFile2) ?? NettoyerUrl(imageUrl2);
        existant.Image3 = await SauvegarderImage(imageFile3) ?? NettoyerUrl(imageUrl3);
        existant.Image4 = await SauvegarderImage(imageFile4) ?? NettoyerUrl(imageUrl4);

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Produit \"{existant.Nom}\" modifié avec succès.";
        return RedirectToAction(nameof(Produits));
    }

    [HttpPost("produits/supprimer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerProduit(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var produit = await _db.Produits.FindAsync(id);
        if (produit == null) return NotFound();
        _db.Produits.Remove(produit);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Produit \"{produit.Nom}\" supprimé.";
        return RedirectToAction(nameof(Produits));
    }

    // ── Commandes ─────────────────────────────────────────────────────────────

    [HttpGet("commandes")]
    public async Task<IActionResult> Commandes(int page = 1, string? q = null, StatutCommande? statut = null, DateTime? dateDebut = null, DateTime? dateFin = null)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        const int pageSize = 50;

        var query = _db.Commandes
            .Include(c => c.Utilisateur)
            .Include(c => c.Lignes)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(c => c.NumeroCommande.Contains(q)
                                  || (c.Utilisateur != null && (c.Utilisateur.Email.Contains(q) || c.Utilisateur.Nom.Contains(q) || c.Utilisateur.Prenom.Contains(q)))
                                  || c.VilleLivraison.Contains(q));
        }
        if (statut.HasValue)    query = query.Where(c => c.Statut == statut.Value);
        if (dateDebut.HasValue) query = query.Where(c => c.DateCommande >= dateDebut.Value);
        if (dateFin.HasValue)   query = query.Where(c => c.DateCommande <= dateFin.Value.AddDays(1).AddSeconds(-1));

        ViewBag.Total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Pages = (int)Math.Ceiling((int)ViewBag.Total / (double)pageSize);
        ViewBag.Q = q;
        ViewBag.Statut = statut;
        ViewBag.DateDebut = dateDebut;
        ViewBag.DateFin = dateFin;

        var data = await query
            .OrderByDescending(c => c.DateCommande)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return View(data);
    }

    [HttpPost("commandes/statut")]
    public async Task<IActionResult> MettreAJourStatut(int id, StatutCommande statut)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        await _commandeService.MettreAJourStatutAsync(id, statut);
        return RedirectToAction(nameof(Commandes));
    }

    // ── Carte des villes clients ─────────────────────────────────────────────
    private static readonly Dictionary<string, (double Lat, double Lng)> CoordVilles =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "Casablanca",  (33.5731, -7.5898) }, { "Rabat",     (34.0209, -6.8417) },
        { "Fès",         (34.0181, -5.0078) }, { "Fes",       (34.0181, -5.0078) },
        { "Marrakech",   (31.6295, -7.9811) }, { "Agadir",    (30.4278, -9.5981) },
        { "Tanger",      (35.7595, -5.8340) }, { "Meknès",    (33.8935, -5.5473) },
        { "Meknes",      (33.8935, -5.5473) }, { "Oujda",     (34.6814, -1.9086) },
        { "Kénitra",     (34.2610, -6.5802) }, { "Kenitra",   (34.2610, -6.5802) },
        { "Tétouan",     (35.5785, -5.3684) }, { "Tetouan",   (35.5785, -5.3684) },
        { "El Jadida",   (33.2316, -8.5007) }, { "Mohammedia",(33.6864, -7.3829) },
        { "Safi",        (32.2994, -9.2372) }, { "Beni Mellal",(32.3373,-6.3498) },
        { "Khouribga",   (32.8810, -6.9063) }, { "Settat",    (33.0017, -7.6166) },
        { "Nador",       (35.1681, -2.9335) }, { "Larache",   (35.1933, -6.1557) },
        { "Khémisset",   (33.8244, -6.0658) }, { "Taza",      (34.2167, -4.0167) },
        { "Berkane",     (34.9219, -2.3201) }, { "Errachidia",(31.9314, -4.4244) },
        { "Ouarzazate",  (30.9189, -6.8934) }, { "Dakhla",    (23.6848, -15.9579) },
        { "Laâyoune",    (27.1536, -13.2033) }, { "Laayoune", (27.1536, -13.2033) },
        { "Essaouira",   (31.5085, -9.7595) }, { "Salé",      (34.0531, -6.7985) },
        { "Sale",        (34.0531, -6.7985) }, { "Témara",    (33.9287, -6.9067) }
    };

    [HttpGet("carte-clients")]
    public async Task<IActionResult> CarteClients()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        return View();
    }

    [HttpGet("carte-clients/data")]
    public async Task<IActionResult> CarteClientsData()
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        var brut = await _db.Commandes
            .Where(c => c.Statut != StatutCommande.Annulee && c.VilleLivraison != null && c.VilleLivraison != "")
            .GroupBy(c => c.VilleLivraison)
            .Select(g => new {
                Ville         = g.Key,
                NbCommandes   = g.Count(),
                Total         = g.Sum(c => c.Total),
                NbClients     = g.Select(c => c.UtilisateurId).Distinct().Count()
            })
            .ToListAsync();

        var pts = brut
            .Where(b => CoordVilles.ContainsKey(b.Ville!))
            .Select(b => new {
                ville       = b.Ville,
                lat         = CoordVilles[b.Ville!].Lat,
                lng         = CoordVilles[b.Ville!].Lng,
                nbCommandes = b.NbCommandes,
                nbClients   = b.NbClients,
                total       = b.Total
            })
            .ToList();

        var inconnues = brut.Where(b => !CoordVilles.ContainsKey(b.Ville!))
                             .Select(b => b.Ville).ToList();

        return Json(new {
            villes = pts,
            inconnues,
            stats = new {
                totalVilles    = pts.Count,
                totalCommandes = pts.Sum(p => p.nbCommandes),
                totalClients   = pts.Sum(p => p.nbClients),
                totalCA        = pts.Sum(p => (decimal)p.total)
            }
        });
    }

    // ── Détection de fraude ───────────────────────────────────────────────────
    [HttpGet("fraude")]
    public async Task<IActionResult> Fraude(NiveauRisque? niveau, DecisionFraude? decision,
                                             [FromServices] IFraudeService fraudeSvc)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        ViewBag.Niveau = niveau;
        ViewBag.Decision = decision;
        ViewBag.Stats = new
        {
            Critique = await _db.EvaluationsFraude.CountAsync(e => e.Niveau == NiveauRisque.Critique),
            Eleve    = await _db.EvaluationsFraude.CountAsync(e => e.Niveau == NiveauRisque.Eleve),
            Modere   = await _db.EvaluationsFraude.CountAsync(e => e.Niveau == NiveauRisque.Modere),
            Bloquees = await _db.EvaluationsFraude.CountAsync(e => e.Decision == DecisionFraude.Bloquee),
            AReviser = await _db.EvaluationsFraude.CountAsync(e => e.Decision == DecisionFraude.AReviser),
        };
        var list = await fraudeSvc.ListerAsync(niveau, decision, 200);
        return View(list);
    }

    [HttpPost("fraude/decision")]
    public async Task<IActionResult> FraudeDecision(int id, DecisionFraude decision, string? commentaire,
                                                     [FromServices] IFraudeService fraudeSvc)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var adminId = HttpContext.Session.GetInt32("UtilisateurId");
        await fraudeSvc.DefinirDecisionAsync(id, decision, adminId, commentaire);
        TempData["Success"] = "Décision enregistrée.";
        return RedirectToAction(nameof(Fraude));
    }

    [HttpPost("commandes/suivi")]
    public async Task<IActionResult> DefinirSuivi(int id, Transporteur transporteur, string? numeroSuivi,
                                                  [FromServices] ISuiviColisService suivi)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (transporteur != Transporteur.Aucun && string.IsNullOrWhiteSpace(numeroSuivi))
            numeroSuivi = suivi.GenererNumeroAutomatique(transporteur);
        await suivi.DefinirSuiviAsync(id, transporteur, numeroSuivi);
        TempData["Success"] = "Informations de suivi mises à jour.";
        return RedirectToAction(nameof(Commandes));
    }

    [HttpGet("commandes/{id:int}/facture")]
    public async Task<IActionResult> TelechargerFacture(int id, [FromServices] IFactureService facture)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var pdf = await facture.GenererPdfAsync(id);
        if (pdf == null) return NotFound();
        var c = await _db.Commandes.FindAsync(id);
        var num = facture.GenererNumeroFacture(id, c!.DateCommande);
        return File(pdf, "application/pdf", $"{num}.pdf");
    }

    // ── Clients ───────────────────────────────────────────────────────────────

    [HttpGet("clients")]
    public async Task<IActionResult> Clients(int page = 1, string? q = null, string? ville = null)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        const int pageSize = 50;

        var query = _db.Utilisateurs
            .Where(u => u.Role == "Client" || u.Role == null || u.Role == "");

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(u => u.Nom.Contains(q) || u.Prenom.Contains(q) || u.Email.Contains(q) || u.Telephone.Contains(q));
        }
        if (!string.IsNullOrWhiteSpace(ville))
        {
            query = query.Where(u => u.Ville == ville);
        }

        ViewBag.Total = await query.CountAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Pages = (int)Math.Ceiling((int)ViewBag.Total / (double)pageSize);
        ViewBag.Q = q;
        ViewBag.Ville = ville;
        ViewBag.Villes = await _db.Utilisateurs
            .Where(u => u.Ville != null && u.Ville != "" && (u.Role == "Client" || u.Role == null || u.Role == ""))
            .Select(u => u.Ville)
            .Distinct()
            .OrderBy(v => v)
            .ToListAsync();

        var clients = await query
            .OrderByDescending(u => u.DateInscription)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        // Dernière commande par client (sur la page affichée)
        var ids = clients.Select(c => c.Id).ToList();
        var dernieresCommandes = await _db.Commandes
            .Where(c => ids.Contains(c.UtilisateurId))
            .GroupBy(c => c.UtilisateurId)
            .Select(g => new { UserId = g.Key, Date = g.Max(c => c.DateCommande) })
            .ToDictionaryAsync(x => x.UserId, x => x.Date);
        ViewBag.DernieresCommandes = dernieresCommandes;

        return View(clients);
    }

    [HttpGet("clients/modifier/{id}")]
    public async Task<IActionResult> ModifierClient(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var client = await _db.Utilisateurs.FindAsync(id);
        if (client == null) return NotFound();
        return View(client);
    }

    [HttpPost("clients/modifier/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierClient(int id,
        string prenom, string nom, string email, string? telephone,
        string? ville, string? adresse, DateTime? dateNaissance,
        string role, bool estActif, string? nouveauMotDePasse)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var client = await _db.Utilisateurs.FindAsync(id);
        if (client == null) return NotFound();

        if (email != client.Email && await _db.Utilisateurs.AnyAsync(u => u.Email == email && u.Id != id))
        {
            TempData["Error"] = "Cet email est déjà utilisé par un autre compte.";
            return View(client);
        }

        client.Prenom        = prenom;
        client.Nom           = nom;
        client.Email         = email;
        client.Telephone     = telephone ?? "";
        client.Ville         = ville ?? "";
        client.Adresse       = adresse ?? "";
        client.DateNaissance = dateNaissance;
        // Liste blanche des rôles (évite toute valeur arbitraire)
        client.Role          = (role is "Client" or "Admin" or "SuperAdmin") ? role : "Client";
        client.EstActif      = estActif;
        if (!string.IsNullOrWhiteSpace(nouveauMotDePasse))
            client.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(nouveauMotDePasse);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Compte \"{prenom} {nom}\" mis à jour.";
        return RedirectToAction(nameof(Clients));
    }

    [HttpPost("clients/bloquer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BloquerClient(int id)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var client = await _db.Utilisateurs.FindAsync(id);
        if (client == null || (client.Role != "Client" && client.Role != "" && client.Role != null)) return NotFound();
        client.EstActif = !client.EstActif;
        await _db.SaveChangesAsync();
        TempData["Success"] = client.EstActif ? $"Client \"{client.Prenom} {client.Nom}\" débloqué." : $"Client \"{client.Prenom} {client.Nom}\" bloqué.";
        return RedirectToAction(nameof(Clients));
    }

    [HttpPost("clients/supprimer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerClient(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var client = await _db.Utilisateurs.FindAsync(id);
        if (client == null || (client.Role != "Client" && client.Role != "" && client.Role != null)) return NotFound();
        _db.Utilisateurs.Remove(client);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Client \"{client.Prenom} {client.Nom}\" supprimé.";
        return RedirectToAction(nameof(Clients));
    }

    // ── Gestion des Admins (SuperAdmin uniquement) ────────────────────────────

    [HttpGet("admins")]
    public async Task<IActionResult> Admins()
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var admins = await _db.Utilisateurs
            .Where(u => u.Role == "Admin")
            .OrderByDescending(u => u.DateInscription)
            .ToListAsync();
        return View(admins);
    }

    [HttpGet("admins/creer")]
    public IActionResult CreerAdmin()
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        return View();
    }

    [HttpPost("admins/creer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreerAdmin(string prenom, string nom, string email, string motDePasse)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;

        if (string.IsNullOrWhiteSpace(prenom) || string.IsNullOrWhiteSpace(nom) ||
            string.IsNullOrWhiteSpace(email)  || string.IsNullOrWhiteSpace(motDePasse))
        {
            TempData["Error"] = "Tous les champs sont obligatoires.";
            return View();
        }

        if (await _db.Utilisateurs.AnyAsync(u => u.Email == email))
        {
            TempData["Error"] = "Un compte avec cet email existe déjà.";
            return View();
        }

        var admin = new Utilisateur
        {
            Prenom          = prenom,
            Nom             = nom,
            Email           = email,
            MotDePasseHash  = BCrypt.Net.BCrypt.HashPassword(motDePasse),
            Role            = "Admin",
            EstActif        = true,
            DateInscription = DateTime.UtcNow
        };

        _db.Utilisateurs.Add(admin);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Administrateur \"{prenom} {nom}\" créé avec succès.";
        return RedirectToAction(nameof(Admins));
    }

    [HttpGet("admins/modifier/{id}")]
    public async Task<IActionResult> ModifierAdmin(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var admin = await _db.Utilisateurs.FindAsync(id);
        if (admin == null || admin.Role != "Admin") return NotFound();
        return View(admin);
    }

    [HttpPost("admins/modifier/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierAdmin(int id,
        string prenom, string nom, string email,
        string? nouveauMotDePasse, bool estActif)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var admin = await _db.Utilisateurs.FindAsync(id);
        if (admin == null || admin.Role != "Admin") return NotFound();

        if (email != admin.Email && await _db.Utilisateurs.AnyAsync(u => u.Email == email && u.Id != id))
        {
            TempData["Error"] = "Cet email est déjà utilisé par un autre compte.";
            return View(admin);
        }

        admin.Prenom   = prenom;
        admin.Nom      = nom;
        admin.Email    = email;
        admin.EstActif = estActif;

        if (!string.IsNullOrWhiteSpace(nouveauMotDePasse))
            admin.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(nouveauMotDePasse);

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Administrateur \"{prenom} {nom}\" mis à jour.";
        return RedirectToAction(nameof(Admins));
    }

    [HttpPost("admins/bloquer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BloquerAdmin(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var admin = await _db.Utilisateurs.FindAsync(id);
        if (admin == null || admin.Role != "Admin") return NotFound();
        admin.EstActif = !admin.EstActif;
        await _db.SaveChangesAsync();
        TempData["Success"] = admin.EstActif ? $"Admin \"{admin.Prenom} {admin.Nom}\" débloqué." : $"Admin \"{admin.Prenom} {admin.Nom}\" bloqué.";
        return RedirectToAction(nameof(Admins));
    }

    [HttpPost("admins/supprimer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerAdmin(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var admin = await _db.Utilisateurs.FindAsync(id);
        if (admin == null || admin.Role != "Admin") return NotFound();
        _db.Utilisateurs.Remove(admin);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Administrateur \"{admin.Prenom} {admin.Nom}\" supprimé.";
        return RedirectToAction(nameof(Admins));
    }

    // ── Catégories ────────────────────────────────────────────────────────────

    [HttpGet("categories")]
    public async Task<IActionResult> Categories()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var cats = await _db.Categories
            .Include(c => c.Parent)
            .Include(c => c.Produits)
            .OrderBy(c => c.ParentId).ThenBy(c => c.Nom)
            .ToListAsync();
        return View(cats);
    }

    // ── Suppression de la catégorie temporaire "À reclasser" + produits ──────
    [HttpPost("categories/supprimer-a-reclasser")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerAReclasser()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Nom == "À reclasser");
        if (cat == null)
        {
            TempData["Error"] = "La catégorie « À reclasser » n'existe pas.";
            return RedirectToAction(nameof(Categories));
        }

        try
        {
            // EF Core retry strategy requires CreateExecutionStrategy() pour transactions manuelles
            var strategy = _db.Database.CreateExecutionStrategy();
            int nbSupprimes = 0;

            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _db.Database.BeginTransactionAsync();

                var produitIds = await _db.Produits.Where(p => p.CategorieId == cat.Id).Select(p => p.Id).ToListAsync();
                if (produitIds.Count > 0)
                {
                    var idsCsv = string.Join(",", produitIds);
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM Avis WHERE ProduitId IN ({idsCsv})");
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM Wishlists WHERE ProduitId IN ({idsCsv})");
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM LignesPanier WHERE ProduitId IN ({idsCsv})");
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM LignesCommande WHERE ProduitId IN ({idsCsv})");
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM RecommandationsProduits WHERE ProduitId IN ({idsCsv}) OR ProduitRecommandeId IN ({idsCsv})");
                    await _db.Database.ExecuteSqlRawAsync($"DELETE FROM Produits WHERE Id IN ({idsCsv})");
                }

                _db.Categories.Remove(cat);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                nbSupprimes = produitIds.Count;
            });

            TempData["Success"] = $"« À reclasser » supprimée ({nbSupprimes} produit(s) supprimé(s)).";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + (ex.InnerException?.Message ?? ex.Message);
        }

        return RedirectToAction(nameof(Categories));
    }

    // ── Réparation : générer des lignes pour les commandes sans lignes ───────
    [HttpPost("categories/reparer-lignes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReparerLignesCommandes()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        try
        {
            // 1) IDs des produits disponibles
            var produitsActifs = await _db.Produits
                .Where(p => p.EstActif)
                .Select(p => new { p.Id, p.Prix, p.PrixPromo })
                .ToListAsync();

            if (produitsActifs.Count == 0)
            {
                TempData["Error"] = "Aucun produit actif en base.";
                return RedirectToAction(nameof(Categories));
            }

            // 2) Commandes sans aucune ligne
            var commandesSansLignes = await _db.Commandes
                .Where(c => !_db.LignesCommande.Any(l => l.CommandeId == c.Id))
                .Select(c => new { c.Id, c.Total })
                .ToListAsync();

            if (commandesSansLignes.Count == 0)
            {
                TempData["Success"] = "Aucune commande à réparer — toutes ont déjà des lignes.";
                return RedirectToAction(nameof(Categories));
            }

            var rng = new Random(42);
            int totalLignes = 0;

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _db.Database.BeginTransactionAsync();
                int batch = 0;
                foreach (var cmd in commandesSansLignes)
                {
                    // 1-4 lignes par commande
                    var nbLignes = rng.Next(1, 5);
                    var produitsChoisis = produitsActifs
                        .OrderBy(_ => rng.Next())
                        .Take(nbLignes)
                        .ToList();

                    foreach (var p in produitsChoisis)
                    {
                        var prix = p.PrixPromo ?? p.Prix;
                        var qte = rng.Next(1, 6);
                        _db.LignesCommande.Add(new LigneCommande
                        {
                            CommandeId   = cmd.Id,
                            ProduitId    = p.Id,
                            Quantite     = qte,
                            PrixUnitaire = prix
                        });
                        totalLignes++;
                        batch++;
                    }
                    if (batch >= 2000)
                    {
                        await _db.SaveChangesAsync();
                        batch = 0;
                    }
                }
                if (batch > 0) await _db.SaveChangesAsync();
                await tx.CommitAsync();
            });

            TempData["Success"] = $"Réparation terminée : {commandesSansLignes.Count:N0} commandes réparées, {totalLignes:N0} lignes créées.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + (ex.InnerException?.Message ?? ex.Message);
        }

        return RedirectToAction(nameof(Categories));
    }

    // ── Diagnostic des données de ventes ────────────────────────────────────
    [HttpGet("diagnostic")]
    public async Task<IActionResult> Diagnostic()
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        var depuis90j = DateTime.UtcNow.AddDays(-90);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<pre style='font-family:monospace; padding:20px; background:#f5f5f5;'>");
        sb.AppendLine("═══════════ DIAGNOSTIC DONNÉES SODIV ═══════════");
        sb.AppendLine($"Heure UTC actuelle : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("🛒 COMMANDES");
        var nbTotalCmd  = await _db.Commandes.CountAsync();
        var nbBigCmd    = await _db.Commandes.CountAsync(c => c.NumeroCommande.StartsWith("BIG-") || c.NumeroCommande.StartsWith("CMD-BIG-"));
        var nbOrigCmd   = nbTotalCmd - nbBigCmd;
        sb.AppendLine($"   Total           : {nbTotalCmd,12:N0}");
        sb.AppendLine($"     ├─ Originales : {nbOrigCmd,12:N0}  (CMD-XXXX-...)");
        sb.AppendLine($"     └─ Big Data   : {nbBigCmd,12:N0}  (BIG-... à supprimer si > 0)");
        sb.AppendLine();
        sb.AppendLine("📋 LIGNES COMMANDE");
        sb.AppendLine($"   Total           : {await _db.LignesCommande.CountAsync():N0}");
        sb.AppendLine();
        sb.AppendLine("📦 PRODUITS");
        var nbTotalProd = await _db.Produits.CountAsync();
        var nbBigProd   = await _db.Produits.CountAsync(p => p.Reference.StartsWith("BIG-"));
        var nbOrigProd  = nbTotalProd - nbBigProd;
        sb.AppendLine($"   Total           : {nbTotalProd,12:N0}");
        sb.AppendLine($"     ├─ Originaux  : {nbOrigProd,12:N0}  (SODIV-... · attendu : 1 209)");
        sb.AppendLine($"     └─ Big Data   : {nbBigProd,12:N0}  (BIG-... à supprimer si > 0)");
        sb.AppendLine();
        sb.AppendLine("👥 CLIENTS");
        sb.AppendLine($"   Total           : {await _db.Utilisateurs.CountAsync(u => u.Role == "Client"),12:N0}");
        sb.AppendLine();
        sb.AppendLine("🏷️ CATÉGORIES");
        sb.AppendLine($"   Total           : {await _db.Categories.CountAsync(),12:N0}  (attendu : 238)");
        sb.AppendLine();
        sb.AppendLine("🗺️ VILLES (top 10)");
        var villes = await _db.Commandes
            .Where(c => c.VilleLivraison != null && c.VilleLivraison != "")
            .GroupBy(c => c.VilleLivraison)
            .Select(g => new { Ville = g.Key, Nb = g.Count() })
            .OrderByDescending(x => x.Nb)
            .Take(10)
            .ToListAsync();
        foreach (var v in villes)
            sb.AppendLine($"   {v.Ville,-25} : {v.Nb} commandes");
        sb.AppendLine("</pre>");
        return Content(sb.ToString(), "text/html; charset=utf-8");
    }

    // ── Téléchargement des CSV de fake data ──────────────────────────────────
    [HttpGet("categories/telecharger-csv/{type}")]
    public IActionResult TelechargerCsv(string type)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        var nomFichier = type switch
        {
            "users"     => "fake_users.csv",
            "commandes" => "fake_commandes.csv",
            "lignes"    => "fake_lignes_commande.csv",
            _ => null
        };
        if (nomFichier == null) return NotFound("Type invalide. Utilisez : users, commandes ou lignes.");

        var path = Path.Combine(_env.ContentRootPath, "App_Data", nomFichier);
        if (!System.IO.File.Exists(path)) return NotFound($"Fichier {nomFichier} introuvable.");

        var bytes = System.IO.File.ReadAllBytes(path);
        return File(bytes, "text/csv; charset=utf-8", nomFichier);
    }

    // ── Import des fake data (10 ans, 20 000 ventes) ─────────────────────────
    [HttpPost("categories/importer-fake-data")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImporterFakeData()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        var dir = Path.Combine(_env.ContentRootPath, "App_Data");
        var pathUsers     = Path.Combine(dir, "fake_users.csv");
        var pathCommandes = Path.Combine(dir, "fake_commandes.csv");
        var pathLignes    = Path.Combine(dir, "fake_lignes_commande.csv");

        if (!System.IO.File.Exists(pathUsers) || !System.IO.File.Exists(pathCommandes) || !System.IO.File.Exists(pathLignes))
        {
            TempData["Error"] = "Fichiers CSV introuvables dans App_Data/ (fake_users.csv, fake_commandes.csv, fake_lignes_commande.csv).";
            return RedirectToAction(nameof(Categories));
        }

        try
        {
            int nbUsers = 0, nbCmd = 0, nbLignes = 0;
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                using var tx = await _db.Database.BeginTransactionAsync();

                // 1) USERS — créer ceux qui n'existent pas (basé sur Email)
                var emailToId = await _db.Utilisateurs.ToDictionaryAsync(u => u.Email.ToLower(), u => u.Id);
                var lignesUsers = await System.IO.File.ReadAllLinesAsync(pathUsers);
                for (int i = 1; i < lignesUsers.Length; i++)
                {
                    var c = ParseCsv(lignesUsers[i]);
                    if (c.Length < 9) continue;
                    var email = c[3].Trim().ToLower();
                    if (emailToId.ContainsKey(email)) continue;

                    var u = new Utilisateur
                    {
                        Prenom = c[1], Nom = c[2], Email = email,
                        Telephone = c[4], Adresse = c[5], Ville = c[6],
                        MotDePasseHash = "$2a$11$FakeHashedPasswordPlaceholder0000000000000000000",
                        Role = "Client", EstActif = true,
                        DateInscription = DateTime.Parse(c[8])
                    };
                    _db.Utilisateurs.Add(u);
                    nbUsers++;
                }
                await _db.SaveChangesAsync();

                // Recharger le mapping email → id (avec les nouveaux)
                emailToId = await _db.Utilisateurs.ToDictionaryAsync(u => u.Email.ToLower(), u => u.Id);
                // Mapping référence produit → id
                var refToProduit = await _db.Produits.ToDictionaryAsync(p => p.Reference, p => p.Id);
                // Mapping NumeroCommande pour idempotence
                var existingCmd = (await _db.Commandes.Select(c => c.NumeroCommande).ToListAsync()).ToHashSet();

                // 2) COMMANDES
                var lignesCmd = await System.IO.File.ReadAllLinesAsync(pathCommandes);
                var noToCmdId = new Dictionary<int, int>(); // CommandeRef CSV → CommandeId DB
                int batchSize = 0;
                var pendingMap = new Dictionary<Commande, int>(); // commande → CommandeRef CSV
                int csvCmdRef = 0;
                for (int i = 1; i < lignesCmd.Length; i++)
                {
                    var c = ParseCsv(lignesCmd[i]);
                    if (c.Length < 13) continue;
                    csvCmdRef++;
                    if (existingCmd.Contains(c[0])) continue;

                    if (!emailToId.TryGetValue(c[1].Trim().ToLower(), out var userId)) continue;
                    var cmd = new Commande
                    {
                        NumeroCommande = c[0],
                        UtilisateurId = userId,
                        DateCommande = DateTime.Parse(c[2]),
                        SousTotal = decimal.Parse(c[3], System.Globalization.CultureInfo.InvariantCulture),
                        FraisLivraison = decimal.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture),
                        Total = decimal.Parse(c[5], System.Globalization.CultureInfo.InvariantCulture),
                        Statut = (StatutCommande)int.Parse(c[6]),
                        MethodePaiement = (MethodePaiement)int.Parse(c[7]),
                        AdresseLivraison = c[8], VilleLivraison = c[9], TelephoneLivraison = c[10],
                        DateLivraison = string.IsNullOrWhiteSpace(c[11]) ? null : DateTime.Parse(c[11]),
                        DateExpedition = string.IsNullOrWhiteSpace(c[12]) ? null : DateTime.Parse(c[12]),
                    };
                    _db.Commandes.Add(cmd);
                    pendingMap[cmd] = csvCmdRef;
                    nbCmd++;
                    batchSize++;
                    if (batchSize >= 1000)
                    {
                        await _db.SaveChangesAsync();
                        foreach (var (k, v) in pendingMap) noToCmdId[v] = k.Id;
                        pendingMap.Clear();
                        batchSize = 0;
                    }
                }
                if (pendingMap.Count > 0)
                {
                    await _db.SaveChangesAsync();
                    foreach (var (k, v) in pendingMap) noToCmdId[v] = k.Id;
                }

                // 3) LIGNES — fallback aléatoire si la référence CSV n'existe pas en DB
                var produitsIds = refToProduit.Values.ToList();
                if (produitsIds.Count == 0)
                {
                    throw new InvalidOperationException("Aucun produit en base. Lance d'abord le seed des produits.");
                }
                var rng = new Random(42);

                var lignesL = await System.IO.File.ReadAllLinesAsync(pathLignes);
                int batchL = 0;
                for (int i = 1; i < lignesL.Length; i++)
                {
                    var c = ParseCsv(lignesL[i]);
                    if (c.Length < 5) continue;
                    var cmdRef = int.Parse(c[1]);
                    if (!noToCmdId.TryGetValue(cmdRef, out var cmdId)) continue;
                    // Si la référence produit CSV n'existe pas → fallback aléatoire
                    int produitId = refToProduit.TryGetValue(c[2], out var pid)
                        ? pid
                        : produitsIds[rng.Next(produitsIds.Count)];

                    _db.LignesCommande.Add(new LigneCommande
                    {
                        CommandeId = cmdId,
                        ProduitId  = produitId,
                        Quantite   = int.Parse(c[3]),
                        PrixUnitaire = decimal.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture),
                    });
                    nbLignes++;
                    batchL++;
                    if (batchL >= 2000)
                    {
                        await _db.SaveChangesAsync();
                        batchL = 0;
                    }
                }
                if (batchL > 0) await _db.SaveChangesAsync();

                await tx.CommitAsync();
            });

            TempData["Success"] = $"Import terminé : {nbUsers} clients, {nbCmd:N0} commandes, {nbLignes:N0} lignes.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + (ex.InnerException?.Message ?? ex.Message);
        }

        return RedirectToAction(nameof(Categories));
    }

    private static string[] ParseCsv(string line)
    {
        // Parser CSV simple — gère les ";" mais nos CSV utilisent "," (DictWriter Python par défaut)
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) { fields.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        fields.Add(cur.ToString());
        return fields.ToArray();
    }

    // ── Gestion images des catégories ────────────────────────────────────────
    [HttpGet("categories/images")]
    public async Task<IActionResult> CategoriesImages()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var categories = await _db.Categories
            .OrderBy(c => c.ParentId)
            .ThenBy(c => c.Nom)
            .ToListAsync();
        ViewBag.Categories = categories;
        return View();
    }

    [HttpPost("categories/{id:int}/image")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierImageCategorie(int id, string? imageUrl, IFormFile? imageFile)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();

        try
        {
            // Cas 1 : upload de fichier
            if (imageFile != null && imageFile.Length > 0)
            {
                var ext = Path.GetExtension(imageFile.FileName).ToLowerInvariant();
                if (!new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext))
                {
                    TempData["Error"] = "Format non supporté (jpg, png, webp, gif uniquement).";
                    return RedirectToAction(nameof(CategoriesImages));
                }
                if (imageFile.Length > 5 * 1024 * 1024)
                {
                    TempData["Error"] = "Image trop grosse (max 5 MB).";
                    return RedirectToAction(nameof(CategoriesImages));
                }

                var dir = Path.Combine(_env.WebRootPath, "images", "categories");
                Directory.CreateDirectory(dir);
                var fileName = $"cat-{id}-{DateTime.UtcNow.Ticks}{ext}";
                var path = Path.Combine(dir, fileName);
                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }
                cat.ImageUrl = $"/images/categories/{fileName}";
            }
            // Cas 2 : URL fournie
            else if (!string.IsNullOrWhiteSpace(imageUrl))
            {
                cat.ImageUrl = imageUrl.Trim();
            }
            // Cas 3 : suppression (champ vide)
            else
            {
                cat.ImageUrl = null;
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Image de « {cat.Nom} » mise à jour.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + ex.Message;
        }

        return RedirectToAction(nameof(CategoriesImages));
    }

    // ── Mise à jour des images : assignation intelligente selon nom+catégorie ─
    [HttpPost("categories/maj-images")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MajImages()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        // Mapping mot-clé du nom/catégorie → image dédiée (Unsplash signature URL)
        // Utilise source.unsplash.com qui renvoie une image différente à chaque ID
        static string ImageDuProduit(string nom, string? marque, string? categorie)
        {
            var s = ((nom ?? "") + " " + (marque ?? "") + " " + (categorie ?? "")).ToLowerInvariant();
            string seed = (nom + categorie ?? "").GetHashCode().ToString("X");

            // Mapping ordre = priorité
            string motCle;
            if      (s.Contains("macbook") || s.Contains("ordinat") && s.Contains("portable")) motCle = "macbook,laptop";
            else if (s.Contains("laptop") || s.Contains("portable") || s.Contains("notebook")) motCle = "laptop,computer";
            else if (s.Contains("imprimante laser"))         motCle = "laser-printer,office";
            else if (s.Contains("imprimante") || s.Contains("printer")) motCle = "printer,office";
            else if (s.Contains("scanner"))                  motCle = "scanner,document";
            else if (s.Contains("smartphone") || s.Contains("iphone") || s.Contains("galaxy")) motCle = "smartphone,phone";
            else if (s.Contains("tablette") || s.Contains("ipad"))     motCle = "tablet,ipad";
            else if (s.Contains("téléphone") || s.Contains("phone"))   motCle = "office-phone";
            else if (s.Contains("écran") || s.Contains("moniteur"))    motCle = "monitor,display";
            else if (s.Contains("clavier"))                  motCle = "keyboard";
            else if (s.Contains("souris") || s.Contains("mouse"))      motCle = "mouse,computer";
            else if (s.Contains("casque") || s.Contains("audio"))      motCle = "headphones";
            else if (s.Contains("haut-parleur") || s.Contains("enceinte")) motCle = "speaker,audio";
            else if (s.Contains("webcam") || s.Contains("caméra"))     motCle = "webcam,camera";
            else if (s.Contains("disque dur") || s.Contains("hdd") || s.Contains("ssd")) motCle = "hard-drive,storage";
            else if (s.Contains("clé usb") || s.Contains("usb"))       motCle = "usb-drive";
            else if (s.Contains("ram") || s.Contains("mémoire"))       motCle = "ram,memory";
            else if (s.Contains("routeur") || s.Contains("wifi"))      motCle = "router,network";
            else if (s.Contains("switch"))                   motCle = "switch,network";
            else if (s.Contains("onduleur") || s.Contains("ups"))      motCle = "ups,power";
            else if (s.Contains("cartouche") || s.Contains("toner"))   motCle = "ink-cartridge,toner";
            // Mobilier
            else if (s.Contains("fauteuil"))                 motCle = "office-chair,executive";
            else if (s.Contains("siège") || s.Contains("chaise"))      motCle = "office-chair";
            else if (s.Contains("bureau") && (s.Contains("direction") || s.Contains("exécutif"))) motCle = "executive-desk,wood";
            else if (s.Contains("bureau") && s.Contains("réunion"))    motCle = "conference-table";
            else if (s.Contains("bureau"))                   motCle = "office-desk";
            else if (s.Contains("table"))                    motCle = "table,office";
            else if (s.Contains("armoire"))                  motCle = "office-cabinet";
            else if (s.Contains("caisson") || s.Contains("rangement")) motCle = "filing-cabinet";
            else if (s.Contains("bibliothèque"))             motCle = "bookshelf,office";
            // Papeterie
            else if (s.Contains("papier a4") || s.Contains("ramette"))  motCle = "paper-ream,a4";
            else if (s.Contains("papier"))                   motCle = "paper,office-supplies";
            else if (s.Contains("enveloppe"))                motCle = "envelopes,mail";
            else if (s.Contains("étiquette"))                motCle = "labels,sticker";
            // Écriture
            else if (s.Contains("stylo") && s.Contains("bille"))       motCle = "ballpoint-pen";
            else if (s.Contains("stylo") || s.Contains("roller"))      motCle = "pens,writing";
            else if (s.Contains("crayon"))                   motCle = "pencils";
            else if (s.Contains("marqueur") || s.Contains("feutre"))   motCle = "markers,colorful";
            else if (s.Contains("surligneur"))               motCle = "highlighter,colorful";
            else if (s.Contains("gomme"))                    motCle = "eraser";
            // Petite fourniture
            else if (s.Contains("agrafe") && !s.Contains("agrafeuse")) motCle = "staples";
            else if (s.Contains("agrafeuse"))                motCle = "stapler";
            else if (s.Contains("perforateur"))              motCle = "hole-punch";
            else if (s.Contains("trombone"))                 motCle = "paper-clips";
            else if (s.Contains("ciseaux"))                  motCle = "scissors";
            else if (s.Contains("règle"))                    motCle = "ruler";
            else if (s.Contains("post-it") || s.Contains("note"))      motCle = "sticky-notes,colorful";
            else if (s.Contains("colle"))                    motCle = "glue,office";
            else if (s.Contains("scotch") || s.Contains("ruban"))      motCle = "tape,office";
            // Classement
            else if (s.Contains("classeur"))                 motCle = "binder,office";
            else if (s.Contains("chemise") || s.Contains("dossier"))   motCle = "folder,documents";
            else if (s.Contains("archive") || s.Contains("boîte"))     motCle = "archive-box";
            // Cahiers
            else if (s.Contains("cahier"))                   motCle = "notebook,school";
            else if (s.Contains("bloc"))                     motCle = "notepad";
            else if (s.Contains("agenda"))                   motCle = "agenda,planner";
            else if (s.Contains("calendrier"))               motCle = "calendar";
            // Machines
            else if (s.Contains("calculatrice"))             motCle = "calculator";
            else if (s.Contains("destructeur"))              motCle = "shredder,office";
            else if (s.Contains("relieur"))                  motCle = "binding-machine";
            else if (s.Contains("plastifieuse"))             motCle = "laminator";
            else if (s.Contains("massicot"))                 motCle = "paper-cutter";
            else if (s.Contains("cachet") || s.Contains("tampon"))     motCle = "rubber-stamp";
            else if (s.Contains("dateur"))                   motCle = "date-stamp";
            // Présentation
            else if (s.Contains("tableau blanc"))            motCle = "whiteboard,office";
            else if (s.Contains("tableau"))                  motCle = "board,office";
            else if (s.Contains("vidéoprojecteur") || s.Contains("projecteur")) motCle = "projector,presentation";
            else if (s.Contains("écran de projection"))      motCle = "projection-screen";
            // Sacs & trousses
            else if (s.Contains("sac à dos") || s.Contains("cartable")) motCle = "backpack,school";
            else if (s.Contains("trousse"))                  motCle = "pencil-case";
            // Coloriage
            else if (s.Contains("crayons de couleur"))       motCle = "colored-pencils";
            else if (s.Contains("feutres de couleur"))       motCle = "colored-markers";
            else if (s.Contains("peinture") || s.Contains("gouache"))  motCle = "paint,art";
            else if (s.Contains("jeu") || s.Contains("puzzle"))        motCle = "board-game";
            // Services généraux
            else if (s.Contains("aspirateur"))               motCle = "vacuum-cleaner";
            else if (s.Contains("nettoyage") || s.Contains("nettoyant")) motCle = "cleaning-supplies";
            else if (s.Contains("savon"))                    motCle = "soap-dispenser";
            else if (s.Contains("papier hygiénique") || s.Contains("essuie-mains")) motCle = "paper-towels";
            else if (s.Contains("coffre") || s.Contains("sécurité"))   motCle = "safe-box";
            else if (s.Contains("vidéosurveillance") || s.Contains("caméra de surveillance")) motCle = "security-camera";
            else if (s.Contains("extincteur"))               motCle = "fire-extinguisher";
            else if (s.Contains("café") || s.Contains("espresso"))     motCle = "coffee-machine";
            else if (s.Contains("cafetière"))                motCle = "coffee-maker";
            else if (s.Contains("bouilloire"))               motCle = "electric-kettle";
            else if (s.Contains("réfrigérateur") || s.Contains("frigo")) motCle = "mini-fridge";
            else if (s.Contains("climatiseur"))              motCle = "air-conditioner";
            else if (s.Contains("ventilateur"))              motCle = "office-fan";
            else if (s.Contains("rallonge") || s.Contains("multiprise")) motCle = "power-strip";
            else if (s.Contains("pile") || s.Contains("batterie"))     motCle = "batteries";
            // Cadeaux
            else if (s.Contains("coffret cadeau"))           motCle = "gift-box";
            else if (s.Contains("emballage cadeau"))         motCle = "gift-wrapping";
            // Photo Audio Vidéo
            else if (s.Contains("appareil photo") || s.Contains("reflex")) motCle = "camera,photography";
            else if (s.Contains("caméscope"))                motCle = "camcorder";
            else if (s.Contains("trépied"))                  motCle = "tripod,camera";
            // Téléphonie
            else if (s.Contains("chargeur"))                 motCle = "charger,cable";
            else if (s.Contains("power bank"))               motCle = "power-bank";
            else if (s.Contains("smartwatch") || s.Contains("connect"))  motCle = "smartwatch";
            else if (s.Contains("coque"))                    motCle = "phone-case";
            // Fallback intelligent par catégorie
            else if (s.Contains("imprim") || s.Contains("consommable")) motCle = "office,printer";
            else if (s.Contains("informatique"))             motCle = "computer,technology";
            else if (s.Contains("mobilier"))                 motCle = "office-furniture";
            else if (s.Contains("scolaire"))                 motCle = "school-supplies";
            else if (s.Contains("hygiène") || s.Contains("entretien")) motCle = "cleaning,sanitary";
            else if (s.Contains("sécurité"))                 motCle = "security,office";
            else if (s.Contains("alimentation") || s.Contains("réception")) motCle = "office-pantry";
            else if (s.Contains("emballage"))                motCle = "packaging,boxes";
            else                                              motCle = "office-product,workspace";

            // Loremflickr fiable avec lock unique (image fixe par produit)
            return $"https://loremflickr.com/500/500/{motCle}?lock={seed}";
        }

        try
        {
            // Cible : produits sans image, avec placeholder, loremflickr, ou Unsplash (sources non fiables)
            var produits = await _db.Produits
                .Where(p => string.IsNullOrEmpty(p.ImageUrl)
                         || p.ImageUrl.Contains("placeholder")
                         || p.ImageUrl.Contains("loremflickr")
                         || p.ImageUrl.Contains("unsplash"))
                .ToListAsync();

            int updated = 0;
            foreach (var p in produits)
            {
                // Nouvel endpoint SVG local — 100% fiable, toujours pertinent
                p.ImageUrl = $"/produit-image/{p.Id}";
                updated++;
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = $"✅ {updated} produits mis à jour avec des images SVG professionnelles générées localement.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + (ex.InnerException?.Message ?? ex.Message);
        }

        return RedirectToAction(nameof(Categories));
    }

    // ── Seed : 3 produits par catégorie feuille ──────────────────────────────
    [HttpPost("categories/seed-produits")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SeedProduits()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        var sqlPath = Path.Combine(_env.ContentRootPath, "App_Data", "SeedProducts.sql");
        if (!System.IO.File.Exists(sqlPath))
        {
            TempData["Error"] = "Fichier SeedProducts.sql introuvable dans App_Data/.";
            return RedirectToAction(nameof(Categories));
        }

        try
        {
            var sql = await System.IO.File.ReadAllTextAsync(sqlPath);
            if (sql.Length > 0 && sql[0] == '﻿') sql = sql[1..];

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await _db.Database.ExecuteSqlRawAsync(sql);
            });

            var total = await _db.Produits.CountAsync();
            TempData["Success"] = $"Produits créés. Total dans la DB : {total}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur : " + (ex.InnerException?.Message ?? ex.Message);
        }

        return RedirectToAction(nameof(Categories));
    }

    // ── Réinitialisation de l'arbre des catégories SODIV ─────────────────────
    [HttpPost("categories/reinitialiser")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReinitialiserCategoriesSodiv()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (!EstSuperAdmin)
        {
            TempData["Error"] = "Réservé au SuperAdmin.";
            return RedirectToAction(nameof(Categories));
        }

        var sqlPath = Path.Combine(_env.ContentRootPath, "App_Data", "ResetCategories.sql");
        if (!System.IO.File.Exists(sqlPath))
        {
            TempData["Error"] = "Fichier ResetCategories.sql introuvable dans App_Data/.";
            return RedirectToAction(nameof(Categories));
        }

        try
        {
            var sql = await System.IO.File.ReadAllTextAsync(sqlPath);
            if (sql.Length > 0 && sql[0] == '﻿') sql = sql[1..];

            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await _db.Database.ExecuteSqlRawAsync(sql);
            });

            var total = await _db.Categories.CountAsync();
            TempData["Success"] = $"Catégories réinitialisées : {total} catégories créées.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = "Erreur SQL : " + ex.Message;
        }

        return RedirectToAction(nameof(Categories));
    }

    [HttpGet("categories/creer")]
    public async Task<IActionResult> CreerCategorie()
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        ViewBag.Parents = await _db.Categories.Where(c => c.EstActive && c.ParentId == null).ToListAsync();
        return View(new Categorie());
    }

    [HttpPost("categories/creer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreerCategorie(string nom, string? iconeClass, int? parentId, bool estActive)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        if (string.IsNullOrWhiteSpace(nom))
        {
            TempData["Error"] = "Le nom est obligatoire.";
            ViewBag.Parents = await _db.Categories.Where(c => c.EstActive && c.ParentId == null).ToListAsync();
            return View(new Categorie());
        }

        var cat = new Categorie
        {
            Nom        = nom.Trim(),
            Slug       = nom.Trim().ToLower().Replace(" ", "-"),
            IconeClass = string.IsNullOrWhiteSpace(iconeClass) ? "fa-box" : iconeClass.Trim(),
            ParentId   = parentId == 0 ? null : parentId,
            EstActive  = estActive
        };

        _db.Categories.Add(cat);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Catégorie \"{cat.Nom}\" créée avec succès.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpGet("categories/modifier/{id}")]
    public async Task<IActionResult> ModifierCategorie(int id)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();
        ViewBag.Parents = await _db.Categories.Where(c => c.EstActive && c.ParentId == null && c.Id != id).ToListAsync();
        return View(cat);
    }

    [HttpPost("categories/modifier/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierCategorie(int id, string nom, string? iconeClass, int? parentId, bool estActive)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var cat = await _db.Categories.FindAsync(id);
        if (cat == null) return NotFound();

        if (string.IsNullOrWhiteSpace(nom))
        {
            TempData["Error"] = "Le nom est obligatoire.";
            ViewBag.Parents = await _db.Categories.Where(c => c.EstActive && c.ParentId == null && c.Id != id).ToListAsync();
            return View(cat);
        }

        cat.Nom        = nom.Trim();
        cat.Slug       = nom.Trim().ToLower().Replace(" ", "-");
        cat.IconeClass = string.IsNullOrWhiteSpace(iconeClass) ? "fa-box" : iconeClass.Trim();
        cat.ParentId   = parentId == 0 ? null : parentId;
        cat.EstActive  = estActive;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Catégorie \"{cat.Nom}\" modifiée.";
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost("categories/supprimer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerCategorie(int id)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        var cat = await _db.Categories.Include(c => c.Produits).FirstOrDefaultAsync(c => c.Id == id);
        if (cat == null) return NotFound();

        if (cat.Produits.Any())
        {
            TempData["Error"] = $"Impossible de supprimer \"{cat.Nom}\" : {cat.Produits.Count} produit(s) y sont associés. Désactivez-la à la place.";
            return RedirectToAction(nameof(Categories));
        }

        _db.Categories.Remove(cat);
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Catégorie \"{cat.Nom}\" supprimée.";
        return RedirectToAction(nameof(Categories));
    }

    // ── Changer le PIN (SuperAdmin uniquement) ────────────────────────────────

    [HttpGet("parametres/pin")]
    public IActionResult ChangerPin()
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;
        return View();
    }

    [HttpPost("parametres/pin")]
    [ValidateAntiForgeryToken]
    public IActionResult ChangerPin(string pinActuel, string nouveauPin, string confirmerPin)
    {
        if (VerifierSuperAdmin() is { } redirect) return redirect;

        var pinConfig = _config["Admin:PinCode"] ?? "";

        if (pinActuel != pinConfig)
        {
            TempData["Error"] = "PIN actuel incorrect.";
            return View();
        }

        if (string.IsNullOrWhiteSpace(nouveauPin) || nouveauPin.Length < 4 || !nouveauPin.All(char.IsDigit))
        {
            TempData["Error"] = "Le nouveau PIN doit contenir au moins 4 chiffres.";
            return View();
        }

        if (nouveauPin != confirmerPin)
        {
            TempData["Error"] = "Les deux PIN ne correspondent pas.";
            return View();
        }

        // Sauvegarde dans appsettings.json
        var path = Path.Combine(_env.ContentRootPath, "appsettings.json");
        var json = System.IO.File.ReadAllText(path);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["Admin"]!["PinCode"] = nouveauPin;
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.Text.Json.Utf8JsonWriter(ms, new System.Text.Json.JsonWriterOptions { Indented = true });
        node.WriteTo(writer);
        writer.Flush();
        System.IO.File.WriteAllBytes(path, ms.ToArray());

        // Mise à jour immédiate en mémoire
        _config["Admin:PinCode"] = nouveauPin;

        // Réinitialiser la session PIN pour forcer la re-saisie
        HttpContext.Session.Remove("AdminPinOk");

        TempData["Success"] = "Code PIN modifié avec succès. Veuillez le saisir à nouveau.";
        return Redirect(SecretPrefix + "/pin?returnUrl=" + Uri.EscapeDataString(SecretPrefix));
    }

    // ── Segmentation clients (RFM) ────────────────────────────────────────────
    [HttpGet("segmentation")]
    public async Task<IActionResult> Segmentation(SegmentClient? segment,
                                                   [FromServices] ISegmentationService seg)
    {
        if (VerifierAdmin() is { } redirect) return redirect;

        ViewBag.Repartition   = await seg.RepartitionAsync();
        ViewBag.Clients       = await seg.ListerAsync(segment, 100);
        ViewBag.DernierCalcul = await seg.DernierCalculAsync();
        ViewBag.Filtre        = segment;
        return View();
    }

    [HttpPost("segmentation/recalculer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SegmentationRecalculer([FromServices] ISegmentationService seg)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        var n = await seg.RecalculerTousAsync();
        TempData["Success"] = $"Segmentation recalculée : {n} clients analysés.";
        return RedirectToAction(nameof(Segmentation));
    }

    // ── Tracking comportemental ───────────────────────────────────────────────
    [HttpGet("comportement")]
    public async Task<IActionResult> Comportement(int jours, [FromServices] ITrackingService track)
    {
        if (VerifierAdmin() is { } redirect) return redirect;
        if (jours <= 0) jours = 30;
        var depuis = DateTime.UtcNow.AddDays(-jours);

        ViewBag.Jours        = jours;
        ViewBag.Entonnoir    = await track.EntonnoirAsync(depuis);
        ViewBag.TopProduits  = await track.TopProduitsVusAsync(depuis, 10);
        ViewBag.Appareils    = await track.RepartitionAppareilsAsync(depuis);
        ViewBag.ParJour      = await track.EvenementsParJourAsync(depuis);
        ViewBag.Recents      = await track.RecentsAsync(50);

        return View();
    }
}
