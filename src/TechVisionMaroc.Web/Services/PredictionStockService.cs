using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public enum NiveauAlerte
{
    Critique,    // < 7 jours
    Attention,   // 7-14 jours
    Ok,          // > 14 jours, ventes régulières
    Surstock,    // beaucoup de stock + très peu de ventes
    SansVente    // aucune vente sur la période
}

public record PrevisionStock(
    int ProduitId,
    string Nom,
    string Marque,
    string? Categorie,
    int StockActuel,
    double VentesParJour,
    double? JoursRestants,    // null = pas de vente
    double TendancePct,       // % évolution récente vs ancienne (positif = en croissance)
    int VentesTotal90j,
    NiveauAlerte Niveau,
    int QuantiteRecommandee   // suggestion réappro pour couvrir 30 jours
);

public interface IPredictionStockService
{
    Task<List<PrevisionStock>> CalculerToutAsync(int joursHistorique = 90);
}

public class PredictionStockService : IPredictionStockService
{
    private readonly AppDbContext _db;

    public PredictionStockService(AppDbContext db) => _db = db;

    public async Task<List<PrevisionStock>> CalculerToutAsync(int joursHistorique = 90)
    {
        var depuis = DateTime.UtcNow.AddDays(-joursHistorique);
        var milieu = DateTime.UtcNow.AddDays(-joursHistorique / 2);

        // Toutes les lignes de commande non annulées sur la période
        var lignes = await _db.LignesCommande
            .Where(lc => lc.Commande!.Statut != StatutCommande.Annulee
                      && lc.Commande.DateCommande >= depuis)
            .Select(lc => new {
                lc.ProduitId,
                lc.Quantite,
                lc.Commande!.DateCommande
            })
            .ToListAsync();

        // Aggrégation par produit
        var stats = lignes
            .GroupBy(l => l.ProduitId)
            .ToDictionary(g => g.Key, g => new
            {
                Total      = g.Sum(x => x.Quantite),
                Recent     = g.Where(x => x.DateCommande >= milieu).Sum(x => x.Quantite),
                Ancien     = g.Where(x => x.DateCommande <  milieu).Sum(x => x.Quantite),
            });

        var produits = await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.EstActif)
            .ToListAsync();

        var resultats = new List<PrevisionStock>();
        foreach (var p in produits)
        {
            stats.TryGetValue(p.Id, out var s);
            var total  = s?.Total  ?? 0;
            var recent = s?.Recent ?? 0;
            var ancien = s?.Ancien ?? 0;

            var ventesParJour = (double)total / joursHistorique;
            double? joursRestants = ventesParJour > 0 ? p.Stock / ventesParJour : (double?)null;

            // Tendance : (récent - ancien) / max(ancien, 1) → en %
            var tendance = ancien > 0
                ? (recent - ancien) * 100.0 / ancien
                : (recent > 0 ? 100.0 : 0.0);

            NiveauAlerte niveau;
            if (total == 0)
                niveau = p.Stock > 20 ? NiveauAlerte.Surstock : NiveauAlerte.SansVente;
            else if (joursRestants is < 7)
                niveau = NiveauAlerte.Critique;
            else if (joursRestants is < 14)
                niveau = NiveauAlerte.Attention;
            else if (joursRestants is > 120 && ventesParJour < 0.1)
                niveau = NiveauAlerte.Surstock;
            else
                niveau = NiveauAlerte.Ok;

            // Recommandation : couvrir 30 jours selon vitesse récente (poids 0.7) + globale (0.3)
            var vitesseRecente = (double)recent / Math.Max(1, joursHistorique / 2);
            var vitessePonderee = vitesseRecente * 0.7 + ventesParJour * 0.3;
            var qteRecommandee = niveau is NiveauAlerte.Critique or NiveauAlerte.Attention
                ? Math.Max(0, (int)Math.Ceiling(vitessePonderee * 30) - p.Stock)
                : 0;

            resultats.Add(new PrevisionStock(
                p.Id, p.Nom, p.Marque, p.Categorie?.Nom,
                p.Stock, Math.Round(ventesParJour, 2), joursRestants.HasValue ? Math.Round(joursRestants.Value, 1) : null,
                Math.Round(tendance, 1), total, niveau, qteRecommandee));
        }

        // Tri : critique d'abord
        return resultats
            .OrderBy(r => r.Niveau switch
            {
                NiveauAlerte.Critique  => 0,
                NiveauAlerte.Attention => 1,
                NiveauAlerte.Ok        => 2,
                NiveauAlerte.Surstock  => 3,
                _                      => 4
            })
            .ThenBy(r => r.JoursRestants ?? double.MaxValue)
            .ToList();
    }
}
