using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

public class ProduitController : Controller
{
    private readonly IProduitService _produits;
    private readonly IIAService _ia;
    private readonly IRecommandationCollaborativeService _recoCollab;
    private readonly AppDbContext _db;

    public ProduitController(IProduitService produits, IIAService ia,
        IRecommandationCollaborativeService recoCollab, AppDbContext db)
    {
        _produits = produits;
        _ia = ia;
        _recoCollab = recoCollab;
        _db = db;
    }

    public async Task<IActionResult> Details(int id, [FromServices] ITrackingService track)
    {
        var produit = await _produits.ObtenirParIdAsync(id);
        if (produit == null) return NotFound();

        // Tracking : nom + marque + catégorie du produit consulté
        ProfilClientService.AjouterMots(HttpContext, produit.Nom);
        ProfilClientService.AjouterMots(HttpContext, produit.Marque);
        ProfilClientService.AjouterMots(HttpContext, produit.Categorie?.Nom);

        // Tracking comportemental enrichi
        await track.EnregistrerAsync(TypeEvenement.ProduitVu, HttpContext,
            cibleType: "Produit", cibleId: id, etiquette: produit.Nom, valeur: produit.Prix);

        ViewBag.Recommandations = await _produits.ObtenirRecommandationsAsync(id);
        ViewBag.RecommandationsCollaboratives = await _recoCollab.ObtenirPourProduitAsync(id, 4);

        var userId = HttpContext.Session.GetInt32("UtilisateurId");

        if (userId != null)
        {
            ViewBag.EstConnecte = true;

            ViewBag.ADejaAvis = await _db.Avis
                .AnyAsync(a => a.ProduitId == id && a.UtilisateurId == userId);

            ViewBag.AAchete = await _db.LignesCommande
                .AnyAsync(l => l.ProduitId == id
                             && l.Commande.UtilisateurId == userId
                             && l.Commande.Statut != StatutCommande.Annulee
                             && l.Commande.Statut != StatutCommande.Remboursee);
        }
        else
        {
            ViewBag.EstConnecte  = false;
            ViewBag.ADejaAvis    = false;
            ViewBag.AAchete      = false;
        }

        return View(produit);
    }
}
