using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

public class HomeController : Controller
{
    private readonly IProduitService _produits;
    private readonly AppDbContext _db;

    public HomeController(IProduitService produits, AppDbContext db)
    {
        _produits = produits;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.ProduitsVedettes = await _produits.ObtenirVedetteAsync(8);
        ViewBag.ProduitsTendances = await _produits.ObtenirTendancesAsync(6);
        ViewBag.Categories = await _db.Categories
            .Where(c => c.EstActive && c.ParentId == null)
            .Include(c => c.SousCategories.Where(s => s.EstActive))
            .OrderBy(c => c.Nom)
            .ToListAsync();
        ViewBag.NombreProduits = await _db.Produits.CountAsync(p => p.EstActif);
        ViewBag.NombreClients = await _db.Utilisateurs.CountAsync(u => u.EstActif);
        ViewBag.NombreCommandes = await _db.Commandes.CountAsync();
        // Les recommandations ne sont affichées qu'aux utilisateurs connectés
        // (un visiteur anonyme ne voit pas de section « Recommandé pour vous »).
        var estConnecte = HttpContext.Session.GetInt32("UtilisateurId") != null;
        ViewBag.EstConnecte = estConnecte;
        ViewBag.Recommandations = estConnecte
            ? await ProfilClientService.RecommanderAsync(_db, HttpContext, 6)
            : new List<TechVisionMaroc.Models.Produit>();
        return View();
    }

    public IActionResult Contact() => View();
    public IActionResult APropos() => View();
    public IActionResult FAQ() => View();

    // Pages légales (loi 09-08 & 31-08 Maroc)
    public IActionResult CGV() => View();
    public IActionResult MentionsLegales() => View();
    public IActionResult Confidentialite() => View();
    public IActionResult Cookies() => View();
    public IActionResult Retours() => View();

    // Promotions & parrainage
    public async Task<IActionResult> Promotions()
    {
        ViewBag.Promos = await _db.Produits
            .Where(p => p.EstActif && p.PrixPromo != null && p.PrixPromo < p.Prix)
            .OrderByDescending(p => (p.Prix - p.PrixPromo!.Value) / p.Prix)
            .Take(48)
            .ToListAsync();
        return View();
    }

    public IActionResult Parrainage() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Contact(string nom, string email, string message)
    {
        TempData["Message"] = "Votre message a été envoyé. Nous vous répondrons dans les 24h.";
        return RedirectToAction(nameof(Contact));
    }

    // ─── Newsletter ───────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Newsletter(string email, string? source)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            TempData["NewsletterErreur"] = "Veuillez saisir un email valide.";
            return Redirect(Request.Headers.Referer.ToString().NullSiVide() ?? "/");
        }

        email = email.Trim().ToLowerInvariant();
        var existe = await _db.AbonnesNewsletter.AnyAsync(a => a.Email == email);
        if (!existe)
        {
            _db.AbonnesNewsletter.Add(new AbonneNewsletter
            {
                Email = email,
                Source = source ?? "footer",
                DateInscription = DateTime.UtcNow,
                EstActif = true
            });
            await _db.SaveChangesAsync();
        }

        TempData["NewsletterSuccess"] = "Merci ! Vous recevrez bientôt nos offres exclusives.";
        return Redirect(Request.Headers.Referer.ToString().NullSiVide() ?? "/");
    }
}

internal static class HomeControllerExt
{
    public static string? NullSiVide(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
