using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

public class CommandeController : Controller
{
    private readonly ICommandeService _commandeService;
    private readonly IFactureService _facture;

    public CommandeController(ICommandeService commandeService, IFactureService facture)
    {
        _commandeService = commandeService;
        _facture = facture;
    }

    private int? UtilisateurId => HttpContext.Session.GetInt32("UtilisateurId");

    public async Task<IActionResult> Details(int id)
    {
        if (UtilisateurId == null)
            return RedirectToAction("Connexion", "Account", new { returnUrl = $"/Commande/Details/{id}" });

        var commande = await _commandeService.ObtenirParIdAsync(id, UtilisateurId.Value);
        if (commande == null)
            return NotFound();

        return View(commande);
    }

    public async Task<IActionResult> Facture(int id)
    {
        if (UtilisateurId == null)
            return RedirectToAction("Connexion", "Account", new { returnUrl = $"/Commande/Facture/{id}" });

        // Vérifie que la commande appartient à l'utilisateur
        var commande = await _commandeService.ObtenirParIdAsync(id, UtilisateurId.Value);
        if (commande == null) return NotFound();

        var pdf = await _facture.GenererPdfAsync(id);
        if (pdf == null) return NotFound();

        var num = _facture.GenererNumeroFacture(id, commande.DateCommande);
        return File(pdf, "application/pdf", $"{num}.pdf");
    }
}
