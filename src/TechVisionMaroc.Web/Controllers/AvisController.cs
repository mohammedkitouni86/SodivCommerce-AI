using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Controllers;

public class AvisController : Controller
{
    private readonly AppDbContext _db;

    public AvisController(AppDbContext db) => _db = db;

    private int? UtilisateurId => HttpContext.Session.GetInt32("UtilisateurId");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Soumettre(int produitId, int note, string commentaire)
    {
        var retour = RedirectToAction("Details", "Produit", new { id = produitId });

        // Doit être connecté
        if (UtilisateurId == null)
        {
            TempData["Error"] = "Vous devez être connecté pour laisser un avis.";
            return retour;
        }

        // Note valide
        if (note < 1 || note > 5)
        {
            TempData["Error"] = "La note doit être entre 1 et 5.";
            return retour;
        }

        // Commentaire non vide
        if (string.IsNullOrWhiteSpace(commentaire) || commentaire.Trim().Length < 10)
        {
            TempData["Error"] = "Le commentaire doit contenir au moins 10 caractères.";
            return retour;
        }

        // Contenu offensant
        if (ContientMotOffensant(commentaire))
        {
            TempData["Error"] = "Votre commentaire contient du contenu inapproprié. Merci de le reformuler.";
            return retour;
        }

        // A-t-il acheté ce produit ?
        var aAchete = await _db.LignesCommande
            .AnyAsync(l => l.ProduitId == produitId
                        && l.Commande.UtilisateurId == UtilisateurId
                        && l.Commande.Statut != StatutCommande.Annulee
                        && l.Commande.Statut != StatutCommande.Remboursee);

        if (!aAchete)
        {
            TempData["Error"] = "Vous devez avoir acheté ce produit pour laisser un avis.";
            return retour;
        }

        // Déjà un avis sur ce produit ?
        var dejaAvis = await _db.Avis
            .AnyAsync(a => a.ProduitId == produitId && a.UtilisateurId == UtilisateurId);

        if (dejaAvis)
        {
            TempData["Error"] = "Vous avez déjà laissé un avis pour ce produit.";
            return retour;
        }

        // Créer l'avis
        var avis = new Avis
        {
            ProduitId      = produitId,
            UtilisateurId  = UtilisateurId.Value,
            Note           = note,
            Commentaire    = commentaire.Trim(),
            AnalyseSentiment = note >= 4 ? "Positif" : note == 3 ? "Neutre" : "Négatif",
            EstValide      = true,
            DateCreation   = DateTime.UtcNow
        };

        _db.Avis.Add(avis);

        // Recalculer la note moyenne du produit
        var produit = await _db.Produits.FindAsync(produitId);
        if (produit != null)
        {
            var notes = await _db.Avis
                .Where(a => a.ProduitId == produitId && a.EstValide && a.Id != avis.Id)
                .Select(a => a.Note)
                .ToListAsync();

            notes.Add(note);
            produit.NombreAvis = notes.Count;
            produit.NoteMoyenne = notes.Average();
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = "Merci pour votre avis ! Il est maintenant publié.";
        return retour;
    }

    // ── Filtre contenu offensant (liste de base) ──────────────────────────────

    private static readonly string[] _motsInterdits =
    [
        "merde", "putain", "connard", "salope", "idiot", "imbecile", "nul", "arnaque",
        "escroc", "voleur", "fuck", "shit", "asshole", "bastard", "crap",
        "كلب", "حمار", "غبي", "نيك"
    ];

    private static bool ContientMotOffensant(string texte)
    {
        var lower = texte.ToLowerInvariant();
        return _motsInterdits.Any(m => lower.Contains(m));
    }
}
