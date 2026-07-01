using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

// Construit un profil d'intérêt à partir des actions du client sur le site :
// recherches, produits consultés, catégories cliquées. Stocké en session (RGPD-friendly).
public static class ProfilClientService
{
    private const string CleSession = "ProfilMots";
    private const int    MaxMots    = 30;

    private static readonly HashSet<string> MotsVides = new(StringComparer.OrdinalIgnoreCase)
    {
        "le","la","les","un","une","des","du","de","au","aux","et","ou","mais","pour",
        "par","avec","sans","sur","sous","dans","chez","est","sont","mon","ma","mes",
        "ce","cet","cette","ces","qui","que","quoi","dont","où","plus","moins","très",
        "the","a","an","of","to","and","or","for","with",
        "sodiv","bureau","site","page","produit","produits","article","articles"
    };

    public static void AjouterMots(HttpContext ctx, string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte)) return;

        var nouveaux = texte.ToLowerInvariant()
            .Split(new[] { ' ', '\'', ',', '.', '-', '_', '/', '\\', '!', '?', ';', ':', '(', ')', '\n', '\r', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => m.Length >= 3 && !MotsVides.Contains(m))
            .Distinct()
            .ToList();

        if (nouveaux.Count == 0) return;

        var liste = LireProfil(ctx);
        // Les nouveaux mots vont en tête (plus récents = plus pertinents)
        foreach (var m in nouveaux) liste.Remove(m);
        liste.InsertRange(0, nouveaux);
        if (liste.Count > MaxMots) liste = liste.Take(MaxMots).ToList();

        ctx.Session.SetString(CleSession, JsonSerializer.Serialize(liste));
    }

    public static List<string> LireProfil(HttpContext ctx)
    {
        var json = ctx.Session.GetString(CleSession);
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    public static async Task<List<Produit>> RecommanderAsync(AppDbContext db, HttpContext ctx, int limite = 6)
    {
        var mots = LireProfil(ctx).Take(15).ToList();

        // 1) Recommandation personnalisée si l'utilisateur a un historique de navigation.
        if (mots.Count > 0)
        {
            var candidats = await db.Produits
                .Where(p => p.EstActif)
                .Include(p => p.Categorie)
                .ToListAsync();

            var personnalisees = candidats
                .Select(p => new { Produit = p, Score = Scorer(p, mots) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Produit.NombreVentes)
                .ThenByDescending(x => x.Produit.NoteMoyenne)
                .Take(limite)
                .Select(x => x.Produit)
                .ToList();

            if (personnalisees.Count > 0) return personnalisees;
        }

        // 2) Cold-start (aucun historique ou aucun match) : produits populaires.
        return await db.Produits
            .Where(p => p.EstActif)
            .Include(p => p.Categorie)
            .OrderByDescending(p => p.NombreVentes)
            .ThenByDescending(p => p.NoteMoyenne)
            .Take(limite)
            .ToListAsync();
    }

    private static int Scorer(Produit p, List<string> mots)
    {
        int score = 0;
        var nom    = p.Nom?.ToLowerInvariant() ?? "";
        var marque = p.Marque?.ToLowerInvariant() ?? "";
        var desc   = p.Description?.ToLowerInvariant() ?? "";
        var cat    = p.Categorie?.Nom?.ToLowerInvariant() ?? "";

        for (int i = 0; i < mots.Count; i++)
        {
            var m = mots[i];
            // Les mots récents (début de liste) pèsent plus
            int poids = mots.Count - i;
            if (nom.Contains(m))    score += 4 * poids;
            if (marque.Contains(m)) score += 3 * poids;
            if (cat.Contains(m))    score += 2 * poids;
            if (desc.Contains(m))   score += 1 * poids;
        }
        return score;
    }
}
