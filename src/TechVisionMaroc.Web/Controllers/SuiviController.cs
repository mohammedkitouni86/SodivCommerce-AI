using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("suivi")]
public class SuiviController : Controller
{
    private readonly ISuiviColisService _suivi;

    public SuiviController(ISuiviColisService suivi) { _suivi = suivi; }

    /// <summary>Page d'accueil pour saisir un n° de commande / suivi.</summary>
    [HttpGet("")]
    public IActionResult Index() => View();

    /// <summary>Formulaire POST → redirige vers la page de détail.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public IActionResult Index(string numero, string? numeroSuivi)
    {
        if (string.IsNullOrWhiteSpace(numero))
        {
            TempData["Error"] = "Veuillez saisir un numéro de commande.";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Detail), new { numero = numero.Trim(), key = numeroSuivi });
    }

    /// <summary>Affiche la timeline de suivi d'un colis (accès public si N° de suivi fourni).</summary>
    [HttpGet("{numero}")]
    public async Task<IActionResult> Detail(string numero, string? key)
    {
        // Si l'utilisateur est connecté → on accepte sans clé
        // Sinon → on exige le n° de suivi comme clé d'authentification light
        var uid = HttpContext.Session.GetInt32("UtilisateurId");
        var commande = await _suivi.ObtenirParNumeroAsync(numero, uid == null ? key : null);

        if (commande == null)
        {
            TempData["Error"] = "Aucun colis trouvé avec ces informations.";
            return RedirectToAction(nameof(Index));
        }

        // Si pas connecté et propriétaire ≠ utilisateur courant → la clé est obligatoire
        if (uid == null && string.IsNullOrWhiteSpace(key))
        {
            TempData["Error"] = "Veuillez saisir le numéro de suivi pour accéder à la page.";
            return RedirectToAction(nameof(Index));
        }
        if (uid != null && uid != commande.UtilisateurId)
        {
            // Un autre utilisateur connecté doit fournir la clé de suivi
            if (string.IsNullOrWhiteSpace(key) || key != commande.NumeroSuivi)
                return Forbid();
        }

        ViewBag.Evenements = _suivi.GenererTimeline(commande);
        ViewBag.Info       = _suivi.Info(commande.Transporteur);
        ViewBag.UrlExterne = _suivi.UrlSuivi(commande.Transporteur, commande.NumeroSuivi);
        return View(commande);
    }
}
