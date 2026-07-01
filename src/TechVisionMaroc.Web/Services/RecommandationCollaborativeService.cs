using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IRecommandationCollaborativeService
{
    Task<List<Produit>> ObtenirPourProduitAsync(int produitId, int nombre = 4);
    Task RecalculerToutAsync();
}

/// <summary>
/// Collaborative Filtering item-item :
/// score(A,B) = |clients qui ont acheté A et B| / |clients qui ont acheté A ou B|  (Jaccard)
/// </summary>
public class RecommandationCollaborativeService : IRecommandationCollaborativeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RecommandationCollaborativeService> _logger;

    public RecommandationCollaborativeService(AppDbContext db, ILogger<RecommandationCollaborativeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<Produit>> ObtenirPourProduitAsync(int produitId, int nombre = 4)
    {
        // 1) Recos pré-calculées
        var recos = await _db.RecommandationsProduits
            .Where(r => r.ProduitId == produitId && r.ProduitRecommande != null && r.ProduitRecommande.EstActif)
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.CoOccurrences)
            .Take(nombre)
            .Include(r => r.ProduitRecommande!).ThenInclude(p => p.Categorie)
            .Select(r => r.ProduitRecommande!)
            .ToListAsync();

        if (recos.Count >= nombre) return recos;

        // 2) Fallback cold-start : même catégorie + prix proche
        var produit = await _db.Produits.FindAsync(produitId);
        if (produit == null) return recos;

        var dejaPris = recos.Select(p => p.Id).Append(produitId).ToHashSet();
        var fourchette = produit.Prix * 0.3m;

        var fallback = await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.EstActif
                     && p.CategorieId == produit.CategorieId
                     && !dejaPris.Contains(p.Id)
                     && p.Prix >= produit.Prix - fourchette
                     && p.Prix <= produit.Prix + fourchette)
            .OrderByDescending(p => p.NombreVentes)
            .ThenByDescending(p => p.NoteMoyenne)
            .Take(nombre - recos.Count)
            .ToListAsync();

        recos.AddRange(fallback);
        return recos;
    }

    public async Task RecalculerToutAsync()
    {
        _logger.LogInformation("🤖 Recalcul des recommandations collaboratives...");
        var debut = DateTime.UtcNow;

        // Charger : (utilisateurId, produitId) pour toutes les commandes non annulées
        var achats = await _db.LignesCommande
            .Where(lc => lc.Commande!.Statut != Models.StatutCommande.Annulee)
            .Select(lc => new { lc.Commande!.UtilisateurId, lc.ProduitId })
            .Distinct()
            .ToListAsync();

        if (achats.Count == 0)
        {
            _logger.LogInformation("Aucun achat → rien à calculer.");
            return;
        }

        // Index : produit → set d'utilisateurs
        var clientsParProduit = achats
            .GroupBy(a => a.ProduitId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UtilisateurId).ToHashSet());

        // Produits actifs
        var produitsActifs = await _db.Produits
            .Where(p => p.EstActif)
            .Select(p => p.Id)
            .ToListAsync();

        var nouvelles = new List<RecommandationProduit>();

        foreach (var a in produitsActifs)
        {
            if (!clientsParProduit.TryGetValue(a, out var clA) || clA.Count == 0) continue;

            foreach (var b in produitsActifs)
            {
                if (b == a) continue;
                if (!clientsParProduit.TryGetValue(b, out var clB) || clB.Count == 0) continue;

                var inter = clA.Intersect(clB).Count();
                if (inter == 0) continue;

                var union = clA.Count + clB.Count - inter;
                var score = (double)inter / union; // Jaccard

                nouvelles.Add(new RecommandationProduit
                {
                    ProduitId = a,
                    ProduitRecommandeId = b,
                    Score = score,
                    CoOccurrences = inter,
                    DateCalcul = DateTime.UtcNow
                });
            }
        }

        // Garder top 20 par produit pour limiter la taille
        nouvelles = nouvelles
            .GroupBy(r => r.ProduitId)
            .SelectMany(g => g.OrderByDescending(r => r.Score).Take(20))
            .ToList();

        // La stratégie de réexécution (EnableRetryOnFailure) interdit les transactions
        // manuelles : on encapsule donc le tout dans une execution strategy retriable.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await _db.Database.ExecuteSqlRawAsync("DELETE FROM RecommandationsProduits");
            await _db.RecommandationsProduits.AddRangeAsync(nouvelles);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        });

        _logger.LogInformation("✅ {Count} recommandations calculées en {Sec}s",
            nouvelles.Count, (DateTime.UtcNow - debut).TotalSeconds.ToString("F1"));
    }
}

/// <summary>Job d'arrière-plan qui recalcule les recommandations chaque nuit (02:00).</summary>
public class RecommandationJob : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RecommandationJob> _logger;

    public RecommandationJob(IServiceProvider services, ILogger<RecommandationJob> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Premier calcul après 30 s (laisser l'app démarrer)
        await Task.Delay(TimeSpan.FromSeconds(30), stop);
        await Executer();

        while (!stop.IsCancellationRequested)
        {
            // Prochain run à 02:00 locale
            var maintenant = DateTime.Now;
            var prochain = maintenant.Date.AddDays(1).AddHours(2);
            var attente = prochain - maintenant;
            try { await Task.Delay(attente, stop); }
            catch (TaskCanceledException) { break; }

            await Executer();
        }
    }

    private async Task Executer()
    {
        try
        {
            using var scope = _services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IRecommandationCollaborativeService>();
            await svc.RecalculerToutAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec recalcul recommandations");
        }
    }
}
