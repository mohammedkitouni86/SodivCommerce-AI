using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Controllers;

/// <summary>
/// Contrôleur pour les fonctionnalités étendues de l'espace client :
/// Historique de navigation, Carnet d'adresses, Coupons, Messagerie, Abonnements, Devis.
/// Toutes les actions exigent un utilisateur connecté.
/// </summary>
[Route("Compte")]
public class EspaceClientController : Controller
{
    private readonly AppDbContext _db;
    public EspaceClientController(AppDbContext db) { _db = db; }

    private int? UserId => HttpContext.Session.GetInt32("UtilisateurId");
    private IActionResult? RequireAuth()
    {
        if (UserId == null) return Redirect("/Account/Connexion?returnUrl=" + Uri.EscapeDataString(Request.Path));
        return null;
    }

    // ── HOME : tableau de bord du compte ───────────────────────────────────────
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        ViewBag.NbCommandes  = await _db.Commandes.CountAsync(c => c.UtilisateurId == uid);
        ViewBag.NbWishlist   = await _db.Wishlists.CountAsync(w => w.UtilisateurId == uid);
        ViewBag.NbAdresses   = await _db.AdressesClient.CountAsync(a => a.UtilisateurId == uid);
        ViewBag.NbCoupons    = await _db.Coupons.CountAsync(c => c.EstActif && c.DateFin >= DateTime.UtcNow
                                && (c.UtilisateurId == null || c.UtilisateurId == uid));
        ViewBag.NbMessages   = await _db.Conversations.CountAsync(c => c.UtilisateurId == uid && c.Statut != StatutConversation.Archivee);
        ViewBag.NbAbonnements= await _db.Abonnements.CountAsync(a => a.UtilisateurId == uid && a.Statut == StatutAbonnement.Actif);
        ViewBag.NbDevis      = await _db.Devis.CountAsync(d => d.UtilisateurId == uid);
        return View();
    }

    // ═══════════════════ HISTORIQUE DE NAVIGATION ═══════════════════
    [HttpGet("historique")]
    public async Task<IActionResult> Historique()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;

        // Sessions de l'utilisateur (par sessionId stockée dans EvenementsComportement)
        // On récupère les produits vus dans les 90 derniers jours
        var depuis = DateTime.UtcNow.AddDays(-90);
        var produitsVus = await _db.EvenementsComportement
            .Where(e => e.UtilisateurId == uid
                     && e.Date >= depuis
                     && e.Type == TypeEvenement.ProduitVu
                     && e.CibleId != null)
            .GroupBy(e => e.CibleId!.Value)
            .Select(g => new {
                ProduitId = g.Key,
                DerniereVue = g.Max(e => e.Date),
                NbVues = g.Count()
            })
            .OrderByDescending(x => x.DerniereVue)
            .Take(60)
            .ToListAsync();

        var ids = produitsVus.Select(x => x.ProduitId).ToList();
        var produits = await _db.Produits
            .Where(p => ids.Contains(p.Id))
            .Include(p => p.Categorie)
            .ToDictionaryAsync(p => p.Id);

        var liste = produitsVus
            .Where(v => produits.ContainsKey(v.ProduitId))
            .Select(v => new {
                Produit = produits[v.ProduitId],
                v.DerniereVue,
                v.NbVues
            })
            .ToList();

        ViewBag.Historique = liste;
        return View();
    }

    [HttpPost("historique/effacer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EffacerHistorique()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        await _db.EvenementsComportement
            .Where(e => e.UtilisateurId == uid && e.Type == TypeEvenement.ProduitVu)
            .ExecuteDeleteAsync();
        TempData["Success"] = "Historique de navigation effacé.";
        return RedirectToAction(nameof(Historique));
    }

    // ═══════════════════ CARNET D'ADRESSES ═══════════════════
    [HttpGet("adresses")]
    public async Task<IActionResult> Adresses()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        ViewBag.Adresses = await _db.AdressesClient
            .Where(a => a.UtilisateurId == uid)
            .OrderByDescending(a => a.ParDefaut)
            .ThenByDescending(a => a.DateCreation)
            .ToListAsync();
        return View();
    }

    [HttpPost("adresses/ajouter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AjouterAdresse(AdresseClient model, bool parDefaut = false)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        model.UtilisateurId = uid;
        model.ParDefaut = parDefaut;
        if (parDefaut)
        {
            // Désactive les autres par défaut
            await _db.AdressesClient.Where(a => a.UtilisateurId == uid && a.ParDefaut)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.ParDefaut, false));
        }
        _db.AdressesClient.Add(model);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Adresse ajoutée.";
        return RedirectToAction(nameof(Adresses));
    }

    [HttpPost("adresses/supprimer/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SupprimerAdresse(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var a = await _db.AdressesClient.FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (a != null) { _db.AdressesClient.Remove(a); await _db.SaveChangesAsync(); }
        TempData["Success"] = "Adresse supprimée.";
        return RedirectToAction(nameof(Adresses));
    }

    [HttpPost("adresses/defaut/{id}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdresseParDefaut(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        await _db.AdressesClient.Where(a => a.UtilisateurId == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ParDefaut, false));
        await _db.AdressesClient.Where(a => a.UtilisateurId == uid && a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ParDefaut, true));
        TempData["Success"] = "Adresse par défaut mise à jour.";
        return RedirectToAction(nameof(Adresses));
    }

    // ═══════════════════ MES COUPONS ═══════════════════
    [HttpGet("coupons")]
    public async Task<IActionResult> Coupons()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var maintenant = DateTime.UtcNow;

        // Tous les coupons actifs : globaux ou personnels au client
        var coupons = await _db.Coupons
            .Where(c => c.EstActif
                     && c.DateFin >= maintenant
                     && (c.UtilisateurId == null || c.UtilisateurId == uid)
                     && (c.LimiteUtilisation == null || c.UtilisationActuelle < c.LimiteUtilisation))
            .OrderBy(c => c.DateFin)
            .ToListAsync();

        // Compter les utilisations passées du client par coupon
        var utilisationsParCoupon = await _db.UtilisationsCoupon
            .Where(u => u.UtilisateurId == uid)
            .GroupBy(u => u.CouponId)
            .Select(g => new { CouponId = g.Key, Nb = g.Count() })
            .ToDictionaryAsync(x => x.CouponId, x => x.Nb);

        ViewBag.Coupons = coupons;
        ViewBag.UtilisationsParCoupon = utilisationsParCoupon;

        // Historique d'utilisation
        ViewBag.Historique = await _db.UtilisationsCoupon
            .Where(u => u.UtilisateurId == uid)
            .Include(u => u.Coupon)
            .OrderByDescending(u => u.DateUtilisation)
            .Take(20)
            .ToListAsync();
        return View();
    }

    // ═══════════════════ MESSAGERIE ═══════════════════
    [HttpGet("messages")]
    public async Task<IActionResult> Messages()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        ViewBag.Conversations = await _db.Conversations
            .Where(c => c.UtilisateurId == uid && c.Statut != StatutConversation.Archivee)
            .OrderByDescending(c => c.DateDernierMessage)
            .ToListAsync();
        return View();
    }

    [HttpGet("messages/{id}")]
    public async Task<IActionResult> MessageDetail(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var conv = await _db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.DateEnvoi))
            .FirstOrDefaultAsync(c => c.Id == id && c.UtilisateurId == uid);
        if (conv == null) return NotFound();
        // Marquer comme lu côté client
        if (!conv.LuParClient) { conv.LuParClient = true; await _db.SaveChangesAsync(); }
        return View(conv);
    }

    [HttpPost("messages/nouveau")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NouveauMessage(string sujet, string contenu, int? commandeId = null, int? produitId = null)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        if (string.IsNullOrWhiteSpace(sujet) || string.IsNullOrWhiteSpace(contenu))
        {
            TempData["Error"] = "Sujet et message sont obligatoires.";
            return RedirectToAction(nameof(Messages));
        }

        var conv = new Conversation
        {
            UtilisateurId = uid,
            Sujet = sujet,
            CommandeId = commandeId,
            ProduitId = produitId,
            Statut = StatutConversation.EnAttente,
            LuParClient = true,
            LuParAdmin = false
        };
        _db.Conversations.Add(conv);
        await _db.SaveChangesAsync();

        _db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Expediteur = ExpediteurMessage.Client,
            Contenu = contenu
        });
        await _db.SaveChangesAsync();

        TempData["Success"] = "Message envoyé. Notre équipe vous répond sous 24h.";
        return RedirectToAction(nameof(MessageDetail), new { id = conv.Id });
    }

    [HttpPost("messages/{id}/repondre")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RepondreMessage(int id, string contenu)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        if (string.IsNullOrWhiteSpace(contenu))
            return RedirectToAction(nameof(MessageDetail), new { id });

        var conv = await _db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.UtilisateurId == uid);
        if (conv == null) return NotFound();

        _db.Messages.Add(new Message
        {
            ConversationId = conv.Id,
            Expediteur = ExpediteurMessage.Client,
            Contenu = contenu
        });
        conv.DateDernierMessage = DateTime.UtcNow;
        conv.Statut = StatutConversation.EnAttente;
        conv.LuParAdmin = false;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(MessageDetail), new { id });
    }

    // ═══════════════════ ABONNEMENTS (Subscribe & Save) ═══════════════════
    [HttpGet("abonnements")]
    public async Task<IActionResult> Abonnements()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        ViewBag.Abonnements = await _db.Abonnements
            .Where(a => a.UtilisateurId == uid)
            .Include(a => a.Produit)
            .OrderByDescending(a => a.DateCreation)
            .ToListAsync();
        return View();
    }

    [HttpPost("abonnements/creer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreerAbonnement(int produitId, int quantite, FrequenceAbonnement frequence, int? adresseId)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var produit = await _db.Produits.FindAsync(produitId);
        if (produit == null) return NotFound();

        var jours = frequence switch
        {
            FrequenceAbonnement.Hebdo15 => 15,
            FrequenceAbonnement.Mensuel => 30,
            FrequenceAbonnement.Bimestriel => 60,
            FrequenceAbonnement.Trimestriel => 90,
            _ => 30
        };

        _db.Abonnements.Add(new Abonnement
        {
            UtilisateurId = uid,
            ProduitId = produitId,
            Quantite = Math.Max(1, quantite),
            Frequence = frequence,
            AdresseId = adresseId,
            ProchaineLivraison = DateTime.UtcNow.AddDays(jours),
            RemiseAbonnement = 5m
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Abonnement créé. Prochaine livraison dans {jours} jours.";
        return RedirectToAction(nameof(Abonnements));
    }

    [HttpPost("abonnements/{id}/pause")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PauseAbonnement(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var a = await _db.Abonnements.FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (a != null) { a.Statut = a.Statut == StatutAbonnement.Actif ? StatutAbonnement.Pause : StatutAbonnement.Actif; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Abonnements));
    }

    [HttpPost("abonnements/{id}/annuler")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnnulerAbonnement(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var a = await _db.Abonnements.FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (a != null) { a.Statut = StatutAbonnement.Annule; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Abonnements));
    }

    // ═══════════════════ MES DEVIS ═══════════════════
    [HttpGet("devis")]
    public async Task<IActionResult> MesDevis()
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        ViewBag.Devis = await _db.Devis
            .Where(d => d.UtilisateurId == uid)
            .Include(d => d.Lignes)
            .OrderByDescending(d => d.DateCreation)
            .ToListAsync();
        return View();
    }

    [HttpGet("devis/{id}")]
    public async Task<IActionResult> DevisDetail(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var d = await _db.Devis
            .Include(x => x.Lignes).ThenInclude(l => l.Produit)
            .FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (d == null) return NotFound();
        return View(d);
    }

    [HttpPost("devis/{id}/accepter")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AccepterDevis(int id)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var d = await _db.Devis.FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (d != null && d.Statut == StatutDevis.Repondu)
        {
            d.Statut = StatutDevis.Accepte;
            d.DateDecision = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Devis accepté ! Nous vous contactons pour finaliser la commande.";
        }
        return RedirectToAction(nameof(DevisDetail), new { id });
    }

    [HttpPost("devis/{id}/refuser")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefuserDevis(int id, string? raison = null)
    {
        if (RequireAuth() is { } r) return r;
        var uid = UserId!.Value;
        var d = await _db.Devis.FirstOrDefaultAsync(x => x.Id == id && x.UtilisateurId == uid);
        if (d != null && d.Statut == StatutDevis.Repondu)
        {
            d.Statut = StatutDevis.Refuse;
            d.DateDecision = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Devis refusé.";
        }
        return RedirectToAction(nameof(MesDevis));
    }
}
