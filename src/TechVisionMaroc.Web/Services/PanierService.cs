using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IPanierService
{
    Task<Panier> ObtenirPanierAsync(int? utilisateurId, string sessionId);
    Task AjouterArticleAsync(int? utilisateurId, string sessionId, int produitId, int quantite = 1);
    Task ModifierQuantiteAsync(int ligneId, int quantite);
    Task SupprimerArticleAsync(int ligneId);
    Task ViderPanierAsync(int panierId);
    Task FusionnerPaniersAsync(string sessionId, int utilisateurId);
}

public class PanierService : IPanierService
{
    private readonly AppDbContext _db;

    public PanierService(AppDbContext db) => _db = db;

    public async Task<Panier> ObtenirPanierAsync(int? utilisateurId, string sessionId)
    {
        Panier? panier = null;

        if (utilisateurId.HasValue)
            panier = await _db.Paniers
                .Include(p => p.Lignes).ThenInclude(l => l.Produit)
                .FirstOrDefaultAsync(p => p.UtilisateurId == utilisateurId);
        else
            panier = await _db.Paniers
                .Include(p => p.Lignes).ThenInclude(l => l.Produit)
                .FirstOrDefaultAsync(p => p.SessionId == sessionId);

        if (panier == null)
        {
            panier = new Panier { UtilisateurId = utilisateurId, SessionId = sessionId };
            _db.Paniers.Add(panier);
            await _db.SaveChangesAsync();
        }

        return panier;
    }

    public async Task AjouterArticleAsync(int? utilisateurId, string sessionId, int produitId, int quantite = 1)
    {
        var panier = await ObtenirPanierAsync(utilisateurId, sessionId);
        var ligne = panier.Lignes.FirstOrDefault(l => l.ProduitId == produitId);

        if (ligne != null)
            ligne.Quantite += quantite;
        else
            panier.Lignes.Add(new LignePanier { PanierId = panier.Id, ProduitId = produitId, Quantite = quantite });

        panier.DateMiseAJour = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task ModifierQuantiteAsync(int ligneId, int quantite)
    {
        var ligne = await _db.LignesPanier.FindAsync(ligneId);
        if (ligne == null) return;

        if (quantite <= 0)
            _db.LignesPanier.Remove(ligne);
        else
            ligne.Quantite = quantite;

        await _db.SaveChangesAsync();
    }

    public async Task SupprimerArticleAsync(int ligneId)
    {
        var ligne = await _db.LignesPanier.FindAsync(ligneId);
        if (ligne != null)
        {
            _db.LignesPanier.Remove(ligne);
            await _db.SaveChangesAsync();
        }
    }

    public async Task ViderPanierAsync(int panierId)
    {
        var lignes = await _db.LignesPanier.Where(l => l.PanierId == panierId).ToListAsync();
        _db.LignesPanier.RemoveRange(lignes);
        await _db.SaveChangesAsync();
    }

    public async Task FusionnerPaniersAsync(string sessionId, int utilisateurId)
    {
        var panierSession = await _db.Paniers
            .Include(p => p.Lignes)
            .FirstOrDefaultAsync(p => p.SessionId == sessionId);

        if (panierSession == null || !panierSession.Lignes.Any()) return;

        var panierUser = await ObtenirPanierAsync(utilisateurId, sessionId);

        foreach (var ligne in panierSession.Lignes)
        {
            var existante = panierUser.Lignes.FirstOrDefault(l => l.ProduitId == ligne.ProduitId);
            if (existante != null)
                existante.Quantite += ligne.Quantite;
            else
                panierUser.Lignes.Add(new LignePanier { PanierId = panierUser.Id, ProduitId = ligne.ProduitId, Quantite = ligne.Quantite });
        }

        _db.Paniers.Remove(panierSession);
        await _db.SaveChangesAsync();
    }
}
