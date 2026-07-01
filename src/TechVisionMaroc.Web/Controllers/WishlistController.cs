using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("wishlist")]
public class WishlistController : Controller
{
    private readonly IWishlistService _service;
    private readonly ITrackingService _track;
    public WishlistController(IWishlistService service, ITrackingService track)
    {
        _service = service;
        _track = track;
    }

    private int? UtilisateurId => HttpContext.Session.GetInt32("UtilisateurId");

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (UtilisateurId is null)
        {
            TempData["Error"] = "Connectez-vous pour voir votre liste de souhaits.";
            return Redirect("/Account/Connexion?retour=/wishlist");
        }
        var liste = await _service.ListerAsync(UtilisateurId.Value);
        return View(liste);
    }

    [HttpPost("basculer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Basculer(int produitId, string? retour)
    {
        if (UtilisateurId is null)
        {
            if (Request.Headers["X-Requested-With"] == "fetch")
                return Json(new { ok = false, login = true });
            return Redirect("/Account/Connexion");
        }
        var ajoute = await _service.BasculerAsync(UtilisateurId.Value, produitId);
        await _track.EnregistrerAsync(
            ajoute ? Models.TypeEvenement.AjoutWishlist : Models.TypeEvenement.RetireWishlist,
            HttpContext, cibleType: "Produit", cibleId: produitId);

        if (Request.Headers["X-Requested-With"] == "fetch")
        {
            var total = await _service.CompterAsync(UtilisateurId.Value);
            return Json(new { ok = true, ajoute, total });
        }

        TempData["Success"] = ajoute ? "❤️ Ajouté à votre liste de souhaits." : "Retiré de votre liste de souhaits.";
        return Redirect(string.IsNullOrWhiteSpace(retour) || !Url.IsLocalUrl(retour) ? "/wishlist" : retour);
    }

    [HttpPost("supprimer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Supprimer(int produitId)
    {
        if (UtilisateurId is null) return Redirect("/Account/Connexion");
        await _service.SupprimerAsync(UtilisateurId.Value, produitId);
        TempData["Success"] = "Produit retiré de votre liste.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("alerte")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BasculerAlerte(int produitId)
    {
        if (UtilisateurId is null) return Redirect("/Account/Connexion");
        var actif = await _service.BasculerAlerteAsync(UtilisateurId.Value, produitId);
        TempData["Success"] = actif ? "🔔 Alerte de prix activée." : "🔕 Alerte de prix désactivée.";
        return RedirectToAction(nameof(Index));
    }
}
