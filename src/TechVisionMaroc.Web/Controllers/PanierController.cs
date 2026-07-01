using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

public class PanierController : Controller
{
    private readonly IPanierService _panier;
    private readonly ICommandeService _commandes;
    private readonly ITrackingService _track;

    public PanierController(IPanierService panier, ICommandeService commandes, ITrackingService track)
    {
        _panier = panier;
        _commandes = commandes;
        _track = track;
    }

    private int? UtilisateurId => HttpContext.Session.GetInt32("UtilisateurId");
    private string SessionId => HttpContext.Session.Id;

    public async Task<IActionResult> Index()
    {
        var panier = await _panier.ObtenirPanierAsync(UtilisateurId, SessionId);
        return View(panier);
    }

    [HttpPost]
    public async Task<IActionResult> Ajouter(int produitId, int quantite = 1)
    {
        await _panier.AjouterArticleAsync(UtilisateurId, SessionId, produitId, quantite);
        await _track.EnregistrerAsync(Models.TypeEvenement.AjoutPanier, HttpContext,
            cibleType: "Produit", cibleId: produitId, valeur: quantite);
        TempData["Success"] = "Produit ajouté au panier !";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ModifierQuantite(int ligneId, int quantite)
    {
        await _panier.ModifierQuantiteAsync(ligneId, quantite);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Supprimer(int ligneId)
    {
        await _panier.SupprimerArticleAsync(ligneId);
        await _track.EnregistrerAsync(Models.TypeEvenement.RetirePanier, HttpContext,
            cibleType: "LignePanier", cibleId: ligneId);
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Commande()
    {
        if (UtilisateurId == null)
            return RedirectToAction("Connexion", "Account", new { returnUrl = "/Panier/Commande" });

        var panier = await _panier.ObtenirPanierAsync(UtilisateurId, SessionId);
        if (!panier.Lignes.Any())
            return RedirectToAction(nameof(Index));

        await _track.EnregistrerAsync(Models.TypeEvenement.CheckoutDemarre, HttpContext,
            cibleType: "Panier", valeur: panier.Total);
        return View(panier);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValiderCommande(string adresse, string ville, string telephone, string methode, string? notes, int pointsFidelite = 0)
    {
        if (UtilisateurId == null)
            return RedirectToAction("Connexion", "Account");

        // Validation de la méthode de paiement (évite un 500 sur valeur invalide/malveillante)
        if (!Enum.TryParse<Models.MethodePaiement>(methode, out var methodePaiement) ||
            !Enum.IsDefined(typeof(Models.MethodePaiement), methodePaiement))
        {
            TempData["Error"] = "Méthode de paiement invalide.";
            return RedirectToAction(nameof(Commande));
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var resultat = await _commandes.CreerCommandeAvecFraudeAsync(
            UtilisateurId.Value, adresse, ville, telephone, methodePaiement, notes, pointsFidelite, ip, ua);

        await _track.EnregistrerAsync(Models.TypeEvenement.CommandePassee, HttpContext,
            cibleType: "Commande", cibleId: resultat.Commande.Id, valeur: resultat.Commande.Total);

        if (resultat.Bloquee)
        {
            TempData["FraudeBloquee"] = "Votre commande a été détectée comme suspecte et a été suspendue. Notre équipe vous contactera sous 24 h pour vérification.";
            return RedirectToAction("Confirmation", new { id = resultat.Commande.Id });
        }

        return RedirectToAction("Confirmation", new { id = resultat.Commande.Id });
    }

    public async Task<IActionResult> Confirmation(int id)
    {
        if (UtilisateurId == null) return RedirectToAction("Connexion", "Account");
        var commande = await _commandes.ObtenirParIdAsync(id, UtilisateurId.Value);
        if (commande == null) return NotFound();
        return View(commande);
    }
}
