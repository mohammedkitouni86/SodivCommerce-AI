using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IWishlistService
{
    Task<List<Wishlist>> ListerAsync(int utilisateurId);
    Task<bool> ContientAsync(int utilisateurId, int produitId);
    Task<bool> BasculerAsync(int utilisateurId, int produitId);
    Task SupprimerAsync(int utilisateurId, int produitId);
    Task<bool> BasculerAlerteAsync(int utilisateurId, int produitId);
    Task<int> CompterAsync(int utilisateurId);
    Task<int> CompterPourProduitAsync(int produitId);
}

public class WishlistService : IWishlistService
{
    private readonly AppDbContext _db;
    public WishlistService(AppDbContext db) => _db = db;

    public Task<List<Wishlist>> ListerAsync(int utilisateurId)
        => _db.Wishlists
            .Include(w => w.Produit!).ThenInclude(p => p.Categorie)
            .Where(w => w.UtilisateurId == utilisateurId)
            .OrderByDescending(w => w.DateAjout)
            .ToListAsync();

    public Task<bool> ContientAsync(int utilisateurId, int produitId)
        => _db.Wishlists.AnyAsync(w => w.UtilisateurId == utilisateurId && w.ProduitId == produitId);

    /// <summary>Ajoute ou retire — renvoie true si ajouté, false si retiré.</summary>
    public async Task<bool> BasculerAsync(int utilisateurId, int produitId)
    {
        var existant = await _db.Wishlists
            .FirstOrDefaultAsync(w => w.UtilisateurId == utilisateurId && w.ProduitId == produitId);

        if (existant != null)
        {
            _db.Wishlists.Remove(existant);
            await _db.SaveChangesAsync();
            return false;
        }

        var produit = await _db.Produits.FindAsync(produitId);
        if (produit == null) return false;

        _db.Wishlists.Add(new Wishlist
        {
            UtilisateurId = utilisateurId,
            ProduitId     = produitId,
            PrixReference = produit.PrixPromo ?? produit.Prix,
            AlertePrix    = true,
            DateAjout     = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task SupprimerAsync(int utilisateurId, int produitId)
    {
        var w = await _db.Wishlists
            .FirstOrDefaultAsync(x => x.UtilisateurId == utilisateurId && x.ProduitId == produitId);
        if (w != null)
        {
            _db.Wishlists.Remove(w);
            await _db.SaveChangesAsync();
        }
    }

    public async Task<bool> BasculerAlerteAsync(int utilisateurId, int produitId)
    {
        var w = await _db.Wishlists
            .FirstOrDefaultAsync(x => x.UtilisateurId == utilisateurId && x.ProduitId == produitId);
        if (w == null) return false;
        w.AlertePrix = !w.AlertePrix;
        await _db.SaveChangesAsync();
        return w.AlertePrix;
    }

    public Task<int> CompterAsync(int utilisateurId)
        => _db.Wishlists.CountAsync(w => w.UtilisateurId == utilisateurId);

    public Task<int> CompterPourProduitAsync(int produitId)
        => _db.Wishlists.CountAsync(w => w.ProduitId == produitId);
}
