using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using TechVisionMaroc.Controllers;
using TechVisionMaroc.Data;

namespace TechVisionMaroc.Services;

/// <summary>
/// Recherche d'images similaires par hash perceptuel (dHash 64 bits).
/// Cache mémoire des hashs calculés à la première utilisation.
/// </summary>
public static class ImageSimilariteService
{
    // Un produit peut avoir plusieurs photos (galerie : ImageUrl + Image2/3/4) :
    // on stocke UN hash PAR image AVEC son URL, pour pouvoir renvoyer la photo qui
    // correspond réellement (et pas seulement l'image principale).
    private static readonly ConcurrentDictionary<int, (ulong Hash, string Url)[]> _cache = new();
    private static DateTime _dernierRafraichissement = DateTime.MinValue;
    private static readonly SemaphoreSlim _verrou = new(1, 1);

    // Seuil de correspondance : une image identique a une distance ~0. On tolère une
    // marge confortable (ré-encodage JPEG/PNG, redimensionnement, recadrage léger,
    // capture d'écran) afin de retrouver le produit dès que l'image existe dans une de
    // ses photos. Au-delà → le produit n'existe pas dans le catalogue.
    private const int SeuilCorrespondance = 12;

    public static async Task<List<(int Id, int Distance, string? MatchedUrl)>> RechercherAsync(
        AppDbContext db, IWebHostEnvironment env, byte[] imageBytes, int max = 24)
    {
        var hashRecherche = CalculerHash(imageBytes);
        if (hashRecherche == 0) return new();

        await RafraichirCacheAsync(db, env);

        // Pour chaque produit : on garde la PHOTO la plus proche (distance + son URL).
        var ordonnes = _cache
            .Select(kv =>
            {
                var meilleurePhoto = kv.Value
                    .Select(e => (Dist: Hamming(e.Hash, hashRecherche), e.Url))
                    .OrderBy(x => x.Dist)
                    .First();
                return (Id: kv.Key, Distance: meilleurePhoto.Dist, MatchedUrl: (string?)meilleurePhoto.Url);
            })
            .OrderBy(x => x.Distance)
            .ToList();

        if (ordonnes.Count == 0) return new();

        var meilleure = ordonnes[0].Distance;

        // Rien d'assez proche → le produit n'existe pas dans le catalogue.
        if (meilleure > SeuilCorrespondance) return new();

        // On renvoie le produit correspondant (et ses quasi-doublons éventuels,
        // à au plus 2 bits du meilleur), du plus proche au moins proche.
        return ordonnes
            .Where(x => x.Distance <= meilleure + 2)
            .Take(max)
            .ToList();
    }

    private static async Task RafraichirCacheAsync(AppDbContext db, IWebHostEnvironment env)
    {
        // Rafraîchir au plus toutes les 10 minutes
        if ((DateTime.UtcNow - _dernierRafraichissement).TotalMinutes < 10 && !_cache.IsEmpty)
            return;

        await _verrou.WaitAsync();
        try
        {
            var produits = await db.Produits
                .Where(p => p.EstActif && !string.IsNullOrEmpty(p.ImageUrl))
                .Select(p => new
                {
                    p.Id, p.Nom, p.Marque, p.ImageUrl, p.Image2, p.Image3, p.Image4,
                    Cat = p.Categorie != null ? p.Categorie.Nom : null,
                    Icone = p.Categorie != null ? p.Categorie.IconeClass : null
                })
                .ToListAsync();

            foreach (var p in produits)
            {
                if (_cache.ContainsKey(p.Id)) continue;

                // Toutes les photos du produit (galerie : principale + Image2/3/4).
                var images = new[] { p.ImageUrl, p.Image2, p.Image3, p.Image4 }
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct();

                var entrees = new List<(ulong Hash, string Url)>();
                foreach (var url in images)
                {
                    try
                    {
                        byte[]? bytes;
                        // Visuel généré (/produit-image/{id}) : on régénère le SVG puis on le rasterise.
                        if (url!.StartsWith("/produit-image") || url.StartsWith("/categorie-image"))
                        {
                            var svg = ProduitImageController.SvgProduit(p.Nom ?? "", p.Marque, p.Cat, p.Icone);
                            bytes = SvgRasterizer.SvgToPng(svg);
                        }
                        else
                        {
                            // Vraie photo uploadée (fichier local /uploads/... ou URL distante).
                            bytes = await ChargerImageAsync(env, url);
                        }

                        if (bytes is null) continue;
                        var h = CalculerHash(bytes);
                        if (h != 0) entrees.Add((h, url));
                    }
                    catch { /* image illisible : ignorer */ }
                }

                if (entrees.Count > 0) _cache[p.Id] = entrees.ToArray();
            }
            _dernierRafraichissement = DateTime.UtcNow;
        }
        finally { _verrou.Release(); }
    }

    private static async Task<byte[]?> ChargerImageAsync(IWebHostEnvironment env, string url)
    {
        if (url.StartsWith("http://") || url.StartsWith("https://"))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            return await http.GetByteArrayAsync(url);
        }
        // Chemin local /images/xxx → wwwroot/images/xxx (protégé contre la traversée de chemin ../)
        var racine = Path.GetFullPath(env.WebRootPath);
        var chemin = Path.GetFullPath(Path.Combine(racine, url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!chemin.StartsWith(racine, StringComparison.OrdinalIgnoreCase) || !File.Exists(chemin)) return null;
        return await File.ReadAllBytesAsync(chemin);
    }

    /// <summary>dHash : différence horizontale sur image 9x8 grayscale → 64 bits.</summary>
    public static ulong CalculerHash(byte[] bytes)
    {
        try
        {
            using var img = Image.Load<Rgba32>(bytes);
            img.Mutate(c => c.Resize(9, 8).Grayscale());

            ulong hash = 0;
            int bit = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var g  = img[x,     y].R;
                    var g2 = img[x + 1, y].R;
                    if (g > g2) hash |= 1UL << bit;
                    bit++;
                }
            }
            return hash;
        }
        catch { return 0; }
    }

    private static int Hamming(ulong a, ulong b)
    {
        return System.Numerics.BitOperations.PopCount(a ^ b);
    }
}
