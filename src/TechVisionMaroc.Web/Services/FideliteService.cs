using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IFideliteService
{
    /// <summary>Valeur en MAD d'un point fidélité.</summary>
    decimal ValeurPoint { get; }
    /// <summary>Réduction maximale en % du total commande (pour éviter abus).</summary>
    decimal PourcentageMaxReduction { get; }
    /// <summary>Durée de validité des points avant expiration.</summary>
    TimeSpan DureeValidite { get; }

    Task<int> SoldeAsync(int utilisateurId);
    Task<List<TransactionFidelite>> HistoriqueAsync(int utilisateurId, int limit = 100);
    Task<int> PointsExpirantBientotAsync(int utilisateurId, TimeSpan delai);
    /// <summary>Maximum de points utilisables sur une commande (limité par solde et % max).</summary>
    Task<int> MaxUtilisableAsync(int utilisateurId, decimal totalCommandeMad);
    Task<int> CrediterAsync(int utilisateurId, decimal montantHt, int? commandeId, string description);
    /// <summary>Crédite un nombre fixe de points (bonus : parrainage, anniversaire…).</summary>
    Task<int> CrediterBonusAsync(int utilisateurId, int points, string description);
    Task<bool> UtiliserAsync(int utilisateurId, int points, int? commandeId, string description);
    Task<int> AnnulerAsync(int utilisateurId, int commandeId);

    /// <summary>Niveau de fidélité d'un utilisateur, basé sur le total dépensé.</summary>
    Task<NiveauFidelite> NiveauAsync(int utilisateurId);
}

public enum NiveauFidelite { Bronze, Argent, Or, Platine }

public class FideliteService : IFideliteService
{
    private readonly AppDbContext _db;
    public FideliteService(AppDbContext db) => _db = db;

    public decimal ValeurPoint => 0.10m;           // 100 pts = 10 MAD
    public decimal PourcentageMaxReduction => 30m; // max 30% du panier
    public TimeSpan DureeValidite => TimeSpan.FromDays(365);

    public async Task<int> SoldeAsync(int utilisateurId)
        => (await _db.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == utilisateurId))?.PointsFidelite ?? 0;

    public Task<List<TransactionFidelite>> HistoriqueAsync(int utilisateurId, int limit = 100)
        => _db.TransactionsFidelite
            .Include(t => t.Commande)
            .Where(t => t.UtilisateurId == utilisateurId)
            .OrderByDescending(t => t.Date)
            .Take(limit)
            .ToListAsync();

    public async Task<int> PointsExpirantBientotAsync(int utilisateurId, TimeSpan delai)
    {
        var deadline = DateTime.UtcNow.Add(delai);
        return await _db.TransactionsFidelite
            .Where(t => t.UtilisateurId == utilisateurId
                     && t.Type == TypeTransactionFidelite.Gagne
                     && t.DateExpiration != null
                     && t.DateExpiration <= deadline
                     && t.DateExpiration > DateTime.UtcNow)
            .SumAsync(t => (int?)t.Points) ?? 0;
    }

    public async Task<int> MaxUtilisableAsync(int utilisateurId, decimal totalCommandeMad)
    {
        var solde = await SoldeAsync(utilisateurId);
        var maxParPourcentage = (int)Math.Floor(totalCommandeMad * (PourcentageMaxReduction / 100m) / ValeurPoint);
        return Math.Max(0, Math.Min(solde, maxParPourcentage));
    }

    public async Task<int> CrediterAsync(int utilisateurId, decimal montantHt, int? commandeId, string description)
    {
        var points = (int)Math.Floor(montantHt); // 1 MAD HT = 1 point
        if (points <= 0) return 0;

        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId);
        if (user == null) return 0;

        user.PointsFidelite += points;
        _db.TransactionsFidelite.Add(new TransactionFidelite
        {
            UtilisateurId  = utilisateurId,
            Type           = TypeTransactionFidelite.Gagne,
            Points         = points,
            CommandeId     = commandeId,
            Description    = description,
            DateExpiration = DateTime.UtcNow.Add(DureeValidite)
        });

        await _db.SaveChangesAsync();
        return points;
    }

    public async Task<int> CrediterBonusAsync(int utilisateurId, int points, string description)
    {
        if (points <= 0) return 0;
        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId);
        if (user == null) return 0;

        user.PointsFidelite += points;
        _db.TransactionsFidelite.Add(new TransactionFidelite
        {
            UtilisateurId  = utilisateurId,
            Type           = TypeTransactionFidelite.Bonus,
            Points         = points,
            Description    = description,
            DateExpiration = DateTime.UtcNow.Add(DureeValidite)
        });
        await _db.SaveChangesAsync();
        return points;
    }

    public async Task<bool> UtiliserAsync(int utilisateurId, int points, int? commandeId, string description)
    {
        if (points <= 0) return true;

        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId);
        if (user == null || user.PointsFidelite < points) return false;

        user.PointsFidelite -= points;
        _db.TransactionsFidelite.Add(new TransactionFidelite
        {
            UtilisateurId = utilisateurId,
            Type          = TypeTransactionFidelite.Utilise,
            Points        = -points,
            CommandeId    = commandeId,
            Description   = description
        });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<int> AnnulerAsync(int utilisateurId, int commandeId)
    {
        var cmd = await _db.Commandes.FirstOrDefaultAsync(c => c.Id == commandeId && c.UtilisateurId == utilisateurId);
        if (cmd == null) return 0;

        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId);
        if (user == null) return 0;

        // Retire les points gagnés, rend les points utilisés
        var delta = cmd.PointsUtilises - cmd.PointsGagnes;
        user.PointsFidelite += delta;

        _db.TransactionsFidelite.Add(new TransactionFidelite
        {
            UtilisateurId = utilisateurId,
            Type          = TypeTransactionFidelite.Annule,
            Points        = delta,
            CommandeId    = commandeId,
            Description   = $"Annulation commande {cmd.NumeroCommande}"
        });

        await _db.SaveChangesAsync();
        return delta;
    }

    public async Task<NiveauFidelite> NiveauAsync(int utilisateurId)
    {
        var totalAn = await _db.Commandes
            .Where(c => c.UtilisateurId == utilisateurId
                     && c.DateCommande >= DateTime.UtcNow.AddYears(-1)
                     && c.Statut != StatutCommande.Annulee
                     && c.Statut != StatutCommande.Remboursee)
            .SumAsync(c => (decimal?)c.Total) ?? 0m;

        if (totalAn >= 20000) return NiveauFidelite.Platine;
        if (totalAn >= 8000)  return NiveauFidelite.Or;
        if (totalAn >= 2000)  return NiveauFidelite.Argent;
        return NiveauFidelite.Bronze;
    }
}
