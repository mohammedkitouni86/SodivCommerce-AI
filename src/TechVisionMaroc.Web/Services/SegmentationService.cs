using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public record StatSegment(SegmentClient Segment, int NbClients, decimal CAMoyen, decimal CATotal);
public record TopClientRFM(int UtilisateurId, string Nom, string Email, int R, int F, int M,
                            int JoursDepuis, int NbCommandes, decimal Total, SegmentClient Segment);

public interface ISegmentationService
{
    /// <summary>Recalcule tous les scores RFM (vide la table puis insère).</summary>
    Task<int> RecalculerTousAsync(CancellationToken ct = default);
    /// <summary>Renvoie la répartition par segment.</summary>
    Task<List<StatSegment>> RepartitionAsync();
    /// <summary>Renvoie les meilleurs / pires clients (filtré par segment optionnel).</summary>
    Task<List<TopClientRFM>> ListerAsync(SegmentClient? segment, int max = 50);
    /// <summary>Renvoie le score d'un utilisateur.</summary>
    Task<ScoreRFM?> ObtenirPourUtilisateurAsync(int utilisateurId);
    /// <summary>Date du dernier calcul (la plus récente).</summary>
    Task<DateTime?> DernierCalculAsync();
}

public class SegmentationService : ISegmentationService
{
    private readonly AppDbContext _db;
    public SegmentationService(AppDbContext db) { _db = db; }

    public async Task<int> RecalculerTousAsync(CancellationToken ct = default)
    {
        var maintenant = DateTime.UtcNow;

        // 1) Agrégation des commandes valides par utilisateur
        var aggregats = await _db.Commandes
            .Where(c => c.Statut != StatutCommande.Annulee
                     && c.Statut != StatutCommande.Remboursee)
            .GroupBy(c => c.UtilisateurId)
            .Select(g => new
            {
                UtilisateurId = g.Key,
                Derniere      = g.Max(c => c.DateCommande),
                Nb            = g.Count(),
                Total         = g.Sum(c => c.Total)
            })
            .ToListAsync(ct);

        if (aggregats.Count == 0)
        {
            // Vider et sortir
            _db.ScoresRFM.RemoveRange(_db.ScoresRFM);
            await _db.SaveChangesAsync(ct);
            return 0;
        }

        // 2) Calcul des quintiles (1-5) pour R, F, M
        var recences   = aggregats.Select(a => (maintenant - a.Derniere).TotalDays).OrderBy(x => x).ToList();
        var frequences = aggregats.Select(a => (double)a.Nb).OrderBy(x => x).ToList();
        var monetaires = aggregats.Select(a => (double)a.Total).OrderBy(x => x).ToList();

        int ScoreR(double recenceJours) => 6 - Quintile(recences, recenceJours); // inversé : moins de jours = meilleur
        int ScoreF(double freq)         => Quintile(frequences, freq);
        int ScoreM(double mont)         => Quintile(monetaires, mont);

        // 3) Vider l'ancien
        _db.ScoresRFM.RemoveRange(_db.ScoresRFM);

        // 4) Insérer les nouveaux
        foreach (var a in aggregats)
        {
            var jours = (int)Math.Round((maintenant - a.Derniere).TotalDays);
            var r = ScoreR(jours);
            var f = ScoreF(a.Nb);
            var m = ScoreM((double)a.Total);

            _db.ScoresRFM.Add(new ScoreRFM
            {
                UtilisateurId           = a.UtilisateurId,
                JoursDepuisDernierAchat = jours,
                NombreCommandes         = a.Nb,
                MontantTotal            = a.Total,
                ScoreR                  = r,
                ScoreF                  = f,
                ScoreM                  = m,
                Segment                 = ClasserSegment(r, f, m),
                DateCalcul              = maintenant
            });
        }

        await _db.SaveChangesAsync(ct);
        return aggregats.Count;
    }

    /// <summary>Renvoie 1..5 selon le quintile (1 = quintile bas, 5 = quintile haut).</summary>
    private static int Quintile(List<double> tries, double valeur)
    {
        if (tries.Count == 0) return 1;
        // Index = rang dans le tableau trié (0-based)
        int rang = tries.BinarySearch(valeur);
        if (rang < 0) rang = ~rang;
        double pct = (double)rang / tries.Count; // 0 à 1
        if (pct < 0.20) return 1;
        if (pct < 0.40) return 2;
        if (pct < 0.60) return 3;
        if (pct < 0.80) return 4;
        return 5;
    }

    /// <summary>Règles standard de classification RFM en 11 segments.</summary>
    private static SegmentClient ClasserSegment(int r, int f, int m)
    {
        var fm = (f + m) / 2.0;

        // Champions : récents + très actifs + grosse dépense
        if (r >= 4 && f >= 4 && m >= 4) return SegmentClient.Champions;

        // Loyaux : actifs récurrents
        if (r >= 3 && fm >= 3) return SegmentClient.Loyaux;

        // Anciens VIP : ne sont plus venus, mais étaient gros acheteurs
        if (r <= 2 && f >= 4 && m >= 4) return SegmentClient.AnciensVIP;

        // À risque : fidèles devenus inactifs
        if (r <= 2 && fm >= 2.5) return SegmentClient.ARisque;

        // Nouveaux clients : très récents mais peu d'achats
        if (r >= 4 && f == 1) return SegmentClient.NouveauxClients;

        // Potentiels fidèles : récents avec début d'historique
        if (r >= 4 && fm >= 2) return SegmentClient.PotentielsFideles;

        // Prometteurs : moyennement récents avec début d'historique
        if (r == 3 && f <= 2) return SegmentClient.Prometteurs;

        // Attention requise : moyens partout, déclin possible
        if (r >= 2 && r <= 3 && fm >= 2) return SegmentClient.AttentionRequise;

        // Sur le point de dormir : peu récents avec faible activité
        if (r >= 2 && r <= 3 && fm < 2) return SegmentClient.SurLePointDeDormir;

        // Perdus : tout au minimum
        if (r == 1 && f == 1 && m == 1) return SegmentClient.Perdus;

        // Endormis : restants
        return SegmentClient.Endormis;
    }

    public async Task<List<StatSegment>> RepartitionAsync()
    {
        var brut = await _db.ScoresRFM
            .GroupBy(s => s.Segment)
            .Select(g => new
            {
                Segment   = g.Key,
                NbClients = g.Count(),
                CAMoyen   = g.Average(s => s.MontantTotal),
                CATotal   = g.Sum(s => s.MontantTotal)
            })
            .ToListAsync();

        return brut
            .Select(x => new StatSegment(x.Segment, x.NbClients, x.CAMoyen, x.CATotal))
            .OrderByDescending(s => s.NbClients)
            .ToList();
    }

    public async Task<List<TopClientRFM>> ListerAsync(SegmentClient? segment, int max = 50)
    {
        var q = _db.ScoresRFM.Include(s => s.Utilisateur).AsQueryable();
        if (segment.HasValue) q = q.Where(s => s.Segment == segment.Value);

        var brut = await q
            .OrderByDescending(s => s.MontantTotal)
            .Take(max)
            .Select(s => new
            {
                s.UtilisateurId,
                Prenom = s.Utilisateur != null ? s.Utilisateur.Prenom : null,
                Nom    = s.Utilisateur != null ? s.Utilisateur.Nom    : null,
                Email  = s.Utilisateur != null ? s.Utilisateur.Email  : "",
                s.ScoreR, s.ScoreF, s.ScoreM,
                s.JoursDepuisDernierAchat, s.NombreCommandes, s.MontantTotal, s.Segment
            })
            .ToListAsync();

        return brut.Select(x => new TopClientRFM(
            x.UtilisateurId,
            (x.Prenom != null || x.Nom != null) ? ($"{x.Prenom} {x.Nom}".Trim()) : "(inconnu)",
            x.Email ?? "",
            x.ScoreR, x.ScoreF, x.ScoreM,
            x.JoursDepuisDernierAchat, x.NombreCommandes, x.MontantTotal, x.Segment
        )).ToList();
    }

    public Task<ScoreRFM?> ObtenirPourUtilisateurAsync(int utilisateurId) =>
        _db.ScoresRFM.FirstOrDefaultAsync(s => s.UtilisateurId == utilisateurId);

    public async Task<DateTime?> DernierCalculAsync()
    {
        var d = await _db.ScoresRFM.MaxAsync(s => (DateTime?)s.DateCalcul);
        return d;
    }
}
