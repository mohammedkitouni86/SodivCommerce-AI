using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Data;
using TechVisionMaroc.Services;
using Microsoft.EntityFrameworkCore;

namespace TechVisionMaroc.Controllers;

public class CatalogueController : Controller
{
    private readonly IProduitService _produits;
    private readonly AppDbContext _db;
    private const int PageSize = 12;

    public CatalogueController(IProduitService produits, AppDbContext db)
    {
        _produits = produits;
        _db = db;
    }

    public async Task<IActionResult> Index(
        string? q, int? categorieId, decimal? prixMin, decimal? prixMax,
        string? tri, int page = 1, string? vue = "grille")
    {
        var (produits, total) = await _produits.RechercherAsync(q, categorieId, prixMin, prixMax, tri, page, PageSize);

        ViewBag.Categories = await _db.Categories
            .Where(c => c.EstActive)
            .Include(c => c.SousCategories.Where(s => s.EstActive))
            .ToListAsync();

        // Tracking profil : recherche + catégorie cliquée
        ProfilClientService.AjouterMots(HttpContext, q);
        if (categorieId.HasValue)
        {
            var catNom = (ViewBag.Categories as List<Models.Categorie>)?
                .FirstOrDefault(c => c.Id == categorieId.Value)?.Nom;
            ProfilClientService.AjouterMots(HttpContext, catNom);
        }
        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = PageSize;
        ViewBag.Pages = (int)Math.Ceiling(total / (double)PageSize);
        ViewBag.Q = q;
        ViewBag.CategorieId = categorieId;
        ViewBag.PrixMin = prixMin;
        ViewBag.PrixMax = prixMax;
        ViewBag.Tri = tri;
        ViewBag.Vue = vue;

        return View("Index", produits);
    }

    [HttpGet("/recherche")]
    public async Task<IActionResult> Recherche(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return RedirectToAction(nameof(Index));

        return await Index(q: q, categorieId: null, prixMin: null, prixMax: null, tri: null, page: 1);
    }

    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RechercheImage(IFormFile? image, [FromServices] IWebHostEnvironment env)
    {
        if (image is null || image.Length == 0)
            return Json(new { erreur = "Aucune image reçue." });
        if (image.Length > 8 * 1024 * 1024)
            return Json(new { erreur = "Image trop volumineuse (max 8 Mo)." });

        try
        {
            using var ms = new MemoryStream();
            await image.CopyToAsync(ms);
            var bytes = ms.ToArray();

            var resultats = await ImageSimilariteService.RechercherAsync(_db, env, bytes, max: 24);
            if (resultats.Count == 0)
                return Json(new { erreur = "Ce produit n'existe pas dans le catalogue." });

            // Stocker les IDs dans la session pour la vue résultats
            var ids = string.Join(",", resultats.Select(r => r.Id));
            HttpContext.Session.SetString("RechercheImageIds", ids);
            return Json(new { redirect = "/Catalogue/ResultatsImage" });
        }
        catch (Exception ex)
        {
            // Ne pas exposer le détail de l'exception au client (fuite d'info).
            Serilog.Log.Error(ex, "Erreur recherche par image");
            return Json(new { erreur = "Erreur lors de l'analyse de l'image. Réessayez." });
        }
    }

    public async Task<IActionResult> ResultatsImage()
    {
        var idsStr = HttpContext.Session.GetString("RechercheImageIds");
        if (string.IsNullOrEmpty(idsStr))
            return RedirectToAction(nameof(Index));

        var ids = idsStr.Split(',').Select(int.Parse).ToList();
        var produits = await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => ids.Contains(p.Id) && p.EstActif)
            .ToListAsync();
        // Préserver l'ordre par similarité
        produits = ids.Select(i => produits.FirstOrDefault(p => p.Id == i))
                      .Where(p => p != null).Cast<Models.Produit>().ToList();

        ViewBag.Categories = await _db.Categories
            .Where(c => c.EstActive)
            .Include(c => c.SousCategories.Where(s => s.EstActive))
            .ToListAsync();
        ViewBag.Total = produits.Count;
        ViewBag.Page = 1;
        ViewBag.Pages = 1;
        ViewBag.Q = "(recherche par image)";
        ViewBag.Vue = "grille";
        ViewBag.RechercheImage = true;
        return View("Index", produits);
    }

    [HttpGet("/api/autocomplete")]
    public async Task<IActionResult> Autocomplete(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 1)
            return Json(new List<string>());

        var suggestions = await _db.Produits
            .Where(p => p.EstActif && (
                p.Nom.StartsWith(q) ||
                p.Marque.StartsWith(q) ||
                EF.Functions.Like(p.Nom,    "% " + q + "%") ||
                EF.Functions.Like(p.Marque, "% " + q + "%")
            ))
            .OrderByDescending(p => p.Nom.StartsWith(q))
            .ThenBy(p => p.Nom)
            .Select(p => new { p.Nom, p.Marque, p.Id })
            .Take(10)
            .ToListAsync();

        return Json(suggestions);
    }
}
