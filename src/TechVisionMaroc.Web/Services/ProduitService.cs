using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IProduitService
{
    Task<(List<Produit> Produits, int Total)> RechercherAsync(string? q, int? categorieId, decimal? prixMin, decimal? prixMax, string? tri, int page, int pageSize);
    Task<Produit?> ObtenirParIdAsync(int id);
    Task<List<Produit>> ObtenirRecommandationsAsync(int produitId, int nombre = 4);
    Task<List<Produit>> ObtenirVedetteAsync(int nombre = 8);
    Task<List<Produit>> ObtenirTendancesAsync(int nombre = 6);
    Task CreerAsync(Produit produit);
    Task MettreAJourAsync(Produit produit);
    Task SupprimerAsync(int id);
}

public class ProduitService : IProduitService
{
    private readonly AppDbContext _db;
    private readonly IIAService _ia;
    private readonly IRechercheSemantiqueService _semantique;
    private readonly ILogger<ProduitService> _logger;

    public ProduitService(AppDbContext db, IIAService ia, IRechercheSemantiqueService semantique, ILogger<ProduitService> logger)
    {
        _db = db;
        _ia = ia;
        _semantique = semantique;
        _logger = logger;
    }

    public async Task<(List<Produit> Produits, int Total)> RechercherAsync(
        string? q, int? categorieId, decimal? prixMin, decimal? prixMax,
        string? tri, int page, int pageSize)
    {
        var query = _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.EstActif);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Mots vides à ignorer (français + anglais courants)
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "le","la","les","un","une","des","de","du","et","ou","a","au","aux","en","pour","par",
                "avec","sans","sur","sous","dans","ce","cette","ces","mon","ma","mes","ton","ta","tes",
                "son","sa","ses","je","tu","il","elle","nous","vous","ils","elles","est","sont","être",
                "the","of","and","or","to","for","with","is","are","be","that","this"
            };

            var bruts = q.Trim()
                .Split(new[] { ' ', '\t', ',', ';', '.', '?', '!', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1 && !stop.Contains(t))
                .ToList();

            foreach (var mot in bruts)
            {
                var m = mot;
                // Si le mot est un nombre → recherche par prix (±15%)
                if (decimal.TryParse(m.Replace(",", "."),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var nb) && nb > 0)
                {
                    var min = nb * 0.85m;
                    var max = nb * 1.15m;
                    var mStr = ((int)nb).ToString();
                    query = query.Where(p =>
                        (p.Prix      >= min && p.Prix      <= max) ||
                        (p.PrixPromo != null && p.PrixPromo >= min && p.PrixPromo <= max) ||
                        EF.Functions.Like(p.Nom,         "%" + mStr + "%") ||
                        EF.Functions.Like(p.Description, "%" + mStr + "%") ||
                        EF.Functions.Like(p.Reference,   "%" + mStr + "%"));
                }
                else
                {
                    // Recherche texte : n'importe où dans nom, marque, description, catégorie, référence
                    query = query.Where(p =>
                        EF.Functions.Like(p.Nom,             "%" + m + "%") ||
                        EF.Functions.Like(p.Marque,          "%" + m + "%") ||
                        EF.Functions.Like(p.Description,     "%" + m + "%") ||
                        EF.Functions.Like(p.Reference,       "%" + m + "%") ||
                        (p.Categorie != null && EF.Functions.Like(p.Categorie.Nom, "%" + m + "%")));
                }
            }
        }

        if (categorieId.HasValue)
        {
            // Règle : si la catégorie est principale (ParentId IS NULL), on agrège
            // uniquement ses enfants directs (niveau 1). Sinon, on affiche seulement
            // les produits propres de la catégorie cliquée.
            var cat = await _db.Categories
                .Where(c => c.Id == categorieId.Value)
                .Select(c => new { c.Id, c.ParentId })
                .FirstOrDefaultAsync();

            if (cat != null && cat.ParentId == null)
            {
                // Catégorie principale → agrégation des enfants directs uniquement
                var enfantsIds = await _db.Categories
                    .Where(c => c.ParentId == cat.Id && c.EstActive)
                    .Select(c => c.Id)
                    .ToListAsync();
                query = query.Where(p => enfantsIds.Contains(p.CategorieId));
            }
            else
            {
                // Sous-catégorie ou sous-sous-catégorie → produits propres uniquement
                query = query.Where(p => p.CategorieId == categorieId);
            }
        }

        if (prixMin.HasValue)
            query = query.Where(p => p.Prix >= prixMin);

        if (prixMax.HasValue)
            query = query.Where(p => p.Prix <= prixMax);

        query = tri switch
        {
            "prix_asc"   => query.OrderBy(p => p.Prix),
            "prix_desc"  => query.OrderByDescending(p => p.Prix),
            "note"       => query.OrderByDescending(p => p.NoteMoyenne),
            "ventes"     => query.OrderByDescending(p => p.NombreVentes),
            _            => query.OrderByDescending(p => p.DateCreation)
        };

        var total = await query.CountAsync();
        var produits = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // 🧠 Recherche sémantique : si peu de résultats sur une requête textuelle, on étend via Groq
        if (total < 3 && !string.IsNullOrWhiteSpace(q) && page == 1)
        {
            try
            {
                var motsEtendus = await _semantique.EtendreAsync(q);
                if (motsEtendus.Count > 0)
                {
                    var dejaIds = produits.Select(p => p.Id).ToHashSet();
                    var qSem = _db.Produits.Include(p => p.Categorie).Where(p => p.EstActif && !dejaIds.Contains(p.Id));
                    if (categorieId.HasValue) qSem = qSem.Where(p => p.CategorieId == categorieId);
                    if (prixMin.HasValue)     qSem = qSem.Where(p => p.Prix >= prixMin);
                    if (prixMax.HasValue)     qSem = qSem.Where(p => p.Prix <= prixMax);

                    // OR sur tous les mots étendus
                    var motsParam = motsEtendus.Take(8).ToList();
                    qSem = qSem.Where(p => motsParam.Any(m =>
                        EF.Functions.Like(p.Nom,         "%" + m + "%") ||
                        EF.Functions.Like(p.Marque,      "%" + m + "%") ||
                        EF.Functions.Like(p.Description, "%" + m + "%") ||
                        (p.Categorie != null && EF.Functions.Like(p.Categorie.Nom, "%" + m + "%"))));

                    var complements = await qSem
                        .OrderByDescending(p => p.NombreVentes)
                        .ThenByDescending(p => p.NoteMoyenne)
                        .Take(pageSize - produits.Count)
                        .ToListAsync();

                    produits.AddRange(complements);
                    total += complements.Count;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recherche sémantique a échoué (ignorée)");
            }
        }

        return (produits, total);
    }

    public async Task<Produit?> ObtenirParIdAsync(int id) =>
        await _db.Produits
            .Include(p => p.Categorie)
            .Include(p => p.Avis.Where(a => a.EstValide))
                .ThenInclude(a => a.Utilisateur)
            .FirstOrDefaultAsync(p => p.Id == id && p.EstActif);

    public async Task<List<Produit>> ObtenirRecommandationsAsync(int produitId, int nombre = 4)
    {
        try
        {
            var ids = await _ia.ObtenirRecommandationsAsync(produitId, nombre);
            return await _db.Produits
                .Include(p => p.Categorie)
                .Where(p => ids.Contains(p.Id) && p.EstActif)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IA indisponible, fallback sur produits vedettes");
            var produit = await _db.Produits.FindAsync(produitId);
            return await _db.Produits
                .Include(p => p.Categorie)
                .Where(p => p.CategorieId == produit!.CategorieId && p.Id != produitId && p.EstActif)
                .OrderByDescending(p => p.NoteMoyenne)
                .Take(nombre)
                .ToListAsync();
        }
    }

    public async Task<List<Produit>> ObtenirVedetteAsync(int nombre = 8) =>
        await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.EstVedette && p.EstActif)
            .OrderByDescending(p => p.NombreVentes)
            .Take(nombre)
            .ToListAsync();

    public async Task<List<Produit>> ObtenirTendancesAsync(int nombre = 6) =>
        await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.EstActif)
            .OrderByDescending(p => p.NombreVentes)
            .Take(nombre)
            .ToListAsync();

    public async Task CreerAsync(Produit produit)
    {
        produit.Reference = $"TVM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpper()}";
        _db.Produits.Add(produit);
        await _db.SaveChangesAsync();
    }

    public async Task MettreAJourAsync(Produit produit)
    {
        _db.Produits.Update(produit);
        await _db.SaveChangesAsync();
    }

    public async Task SupprimerAsync(int id)
    {
        var produit = await _db.Produits.FindAsync(id);
        if (produit != null)
        {
            produit.EstActif = false;
            await _db.SaveChangesAsync();
        }
    }
}
