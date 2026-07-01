using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public record ResultatCreationCommande(Commande Commande, EvaluationFraude? EvaluationFraude, bool Bloquee);

public interface ICommandeService
{
    Task<Commande> CreerCommandeAsync(int utilisateurId, string adresse, string ville, string telephone, MethodePaiement methode, string? notes, int pointsAUtiliser = 0);
    Task<ResultatCreationCommande> CreerCommandeAvecFraudeAsync(int utilisateurId, string adresse, string ville, string telephone, MethodePaiement methode, string? notes, int pointsAUtiliser, string? ip, string? userAgent);
    Task<Commande?> ObtenirParIdAsync(int id, int utilisateurId);
    Task<List<Commande>> ObtenirCommandesUtilisateurAsync(int utilisateurId);
    Task MettreAJourStatutAsync(int id, StatutCommande statut);
    Task<List<Commande>> ObtenirToutesCommandesAsync(int page, int pageSize);
}

public class CommandeService : ICommandeService
{
    private readonly AppDbContext _db;
    private readonly IPanierService _panier;
    private readonly IFideliteService _fidelite;
    private readonly IPrixService _prix;
    private readonly IFraudeService _fraude;

    public CommandeService(AppDbContext db, IPanierService panier, IFideliteService fidelite, IPrixService prix, IFraudeService fraude)
    {
        _db = db;
        _panier = panier;
        _fidelite = fidelite;
        _prix = prix;
        _fraude = fraude;
    }

    public async Task<ResultatCreationCommande> CreerCommandeAvecFraudeAsync(int utilisateurId, string adresse, string ville, string telephone,
        MethodePaiement methode, string? notes, int pointsAUtiliser, string? ip, string? userAgent)
    {
        var commande = await CreerCommandeAsync(utilisateurId, adresse, ville, telephone, methode, notes, pointsAUtiliser);
        var eval = await _fraude.EvaluerCommandeAsync(commande.Id, ip, userAgent);
        return new ResultatCreationCommande(commande, eval, eval.Decision == DecisionFraude.Bloquee);
    }

    public async Task<Commande> CreerCommandeAsync(int utilisateurId, string adresse, string ville, string telephone, MethodePaiement methode, string? notes, int pointsAUtiliser = 0)
    {
        var panier = await _db.Paniers
            .Include(p => p.Lignes).ThenInclude(l => l.Produit)
            .FirstOrDefaultAsync(p => p.UtilisateurId == utilisateurId)
            ?? throw new InvalidOperationException("Panier vide");

        if (!panier.Lignes.Any())
            throw new InvalidOperationException("Panier vide");

        var commande = new Commande
        {
            NumeroCommande = $"TVM-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            UtilisateurId = utilisateurId,
            MethodePaiement = methode,
            AdresseLivraison = adresse,
            VilleLivraison = ville,
            TelephoneLivraison = telephone,
            Notes = notes,
            FraisLivraison = panier.Total >= 500 ? 0 : 30,
        };

        commande.Lignes = panier.Lignes.Select(l => new LigneCommande
        {
            ProduitId = l.ProduitId,
            Quantite = l.Quantite,
            PrixUnitaire = l.Produit.PrixPromo ?? l.Produit.Prix
        }).ToList();

        commande.SousTotal = commande.Lignes.Sum(l => l.Quantite * l.PrixUnitaire);

        // Application des points fidélité (1 point = 0,10 MAD, max 30%)
        var totalAvantReduction = commande.SousTotal + commande.FraisLivraison;
        var maxUtilisable = await _fidelite.MaxUtilisableAsync(utilisateurId, totalAvantReduction);
        var pointsEffectifs = Math.Max(0, Math.Min(pointsAUtiliser, maxUtilisable));
        var reduction = pointsEffectifs * _fidelite.ValeurPoint;

        commande.PointsUtilises    = pointsEffectifs;
        commande.ReductionFidelite = reduction;
        commande.Total             = Math.Max(0, totalAvantReduction - reduction);

        // Calcul des points à créditer (sur le HT, après réduction fidélité)
        decimal totalHt = 0m;
        foreach (var l in panier.Lignes)
        {
            var pu = l.Produit.PrixPromo ?? l.Produit.Prix;
            var st = pu * l.Quantite;
            totalHt += _prix.Ht(st, l.Produit.TauxTVA);
        }
        // Si points utilisés, on les soustrait proportionnellement au HT
        if (totalAvantReduction > 0 && reduction > 0)
            totalHt = Math.Max(0, totalHt - (totalHt * (reduction / totalAvantReduction)));
        commande.PointsGagnes = (int)Math.Floor(totalHt);

        foreach (var ligne in panier.Lignes)
        {
            var produit = ligne.Produit;
            produit.Stock -= ligne.Quantite;
            produit.NombreVentes += ligne.Quantite;
        }

        _db.Commandes.Add(commande);
        await _db.SaveChangesAsync(); // pour obtenir commande.Id

        // Utilisation des points (débit)
        if (pointsEffectifs > 0)
            await _fidelite.UtiliserAsync(utilisateurId, pointsEffectifs, commande.Id,
                $"Utilisation sur commande {commande.NumeroCommande}");

        // Crédit des points gagnés
        if (commande.PointsGagnes > 0)
            await _fidelite.CrediterAsync(utilisateurId, commande.PointsGagnes, commande.Id,
                $"Achat — commande {commande.NumeroCommande}");

        // ── Parrainage : récompense au 1er achat qualifiant du filleul (≥ 500 MAD) ──
        const decimal seuilParrainage = 500m;
        const int bonusParrainage = 500; // 500 points = 50 MAD (1 pt = 0,10 MAD)
        var acheteur = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == utilisateurId);
        if (acheteur is { ParrainId: not null, ParrainageRecompense: false } && commande.Total >= seuilParrainage)
        {
            await _fidelite.CrediterBonusAsync(acheteur.Id, bonusParrainage,
                "Bonus de bienvenue — parrainage 🎁");
            await _fidelite.CrediterBonusAsync(acheteur.ParrainId.Value, bonusParrainage,
                "Récompense parrainage — 1er achat de votre filleul 🎁");
            acheteur.ParrainageRecompense = true;
            await _db.SaveChangesAsync();
        }

        await _panier.ViderPanierAsync(panier.Id);
        await _db.SaveChangesAsync();

        return commande;
    }

    public async Task<Commande?> ObtenirParIdAsync(int id, int utilisateurId) =>
        await _db.Commandes
            .Include(c => c.Lignes).ThenInclude(l => l.Produit)
            .FirstOrDefaultAsync(c => c.Id == id && c.UtilisateurId == utilisateurId);

    public async Task<List<Commande>> ObtenirCommandesUtilisateurAsync(int utilisateurId) =>
        await _db.Commandes
            .Include(c => c.Lignes)
            .Where(c => c.UtilisateurId == utilisateurId)
            .OrderByDescending(c => c.DateCommande)
            .ToListAsync();

    public async Task MettreAJourStatutAsync(int id, StatutCommande statut)
    {
        var commande = await _db.Commandes.FindAsync(id);
        if (commande == null) return;
        commande.Statut = statut;
        if (statut == StatutCommande.Livree) commande.DateLivraison = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<Commande>> ObtenirToutesCommandesAsync(int page, int pageSize) =>
        await _db.Commandes
            .Include(c => c.Utilisateur)
            .Include(c => c.Lignes)
            .OrderByDescending(c => c.DateCommande)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
}
