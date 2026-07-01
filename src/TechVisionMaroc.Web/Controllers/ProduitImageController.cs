using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using TechVisionMaroc.Data;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

/// <summary>
/// Génère des images SVG "placeholder produit" professionnelles à la volée.
/// 100% fiable (pas de dépendance externe), toujours pertinent (icône SVG path inline + nom + couleur).
/// URL : /produit-image/{id}
/// </summary>
[Route("produit-image")]
public class ProduitImageController : Controller
{
    private readonly AppDbContext _db;
    public ProduitImageController(AppDbContext db) { _db = db; }

    // Cache mémoire des PNG rendus (l'image AFFICHÉE = l'image HACHÉE pour la recherche par image).
    private static readonly ConcurrentDictionary<string, byte[]> _pngCache = new();

    [HttpGet("{id:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Get(int id, int w = 500, int h = 500)
    {
        var produit = await _db.Produits
            .Include(p => p.Categorie)
            .Where(p => p.Id == id)
            .Select(p => new {
                p.Id, p.Nom, p.Marque,
                Cat = p.Categorie != null ? p.Categorie.Nom : null,
                Icone = p.Categorie != null ? p.Categorie.IconeClass : null
            })
            .FirstOrDefaultAsync();

        if (produit == null) return NotFound();

        var (color1, color2, pathSvg) = ConfigPourCategorie(produit.Cat, produit.Icone, produit.Nom);
        var svg = GenererSvg(produit.Nom, produit.Marque, color1, color2, pathSvg, w, h);

        // On sert un PNG (rendu déterministe) et non le SVG : ainsi l'image affichée
        // est exactement celle qui est hachée par la recherche par image.
        var cle = $"{id}_{w}_{h}";
        var png = _pngCache.GetOrAdd(cle, _ => SvgRasterizer.SvgToPng(svg) ?? Array.Empty<byte>());
        if (png.Length > 0) return File(png, "image/png");

        // Repli si la rasterisation échoue.
        return Content(svg, "image/svg+xml");
    }

    /// <summary>
    /// Génère le SVG d'un produit (réutilisé par la recherche par image pour rasteriser puis hasher).
    /// </summary>
    public static string SvgProduit(string nom, string? marque, string? categorie, string? iconeClass, int w = 500, int h = 500)
    {
        var (c1, c2, path) = ConfigPourCategorie(categorie, iconeClass, nom);
        return GenererSvg(nom, marque, c1, c2, path, w, h);
    }

    /// <summary>Génère un SVG pour une CATÉGORIE (fallback si admin n'a pas uploadé d'image).</summary>
    [HttpGet("/categorie-image/{id:int}")]
    [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetCategorie(int id, int w = 300, int h = 300)
    {
        var cat = await _db.Categories
            .Where(c => c.Id == id)
            .Select(c => new { c.Nom, c.IconeClass })
            .FirstOrDefaultAsync();

        if (cat == null) return NotFound();

        var (color1, color2, pathSvg) = ConfigPourCategorie(cat.Nom, cat.IconeClass, cat.Nom);
        var svg = GenererSvgCategorie(cat.Nom, color1, color2, pathSvg, w, h);
        return Content(svg, "image/svg+xml");
    }

    private static string GenererSvgCategorie(string nom, string color1, string color2, string pathSvg, int w, int h)
    {
        var nomCourt = nom ?? "Catégorie";
        if (nomCourt.Length > 26) nomCourt = nomCourt.Substring(0, 24) + "…";
        nomCourt = System.Net.WebUtility.HtmlEncode(nomCourt);

        int circleR = Math.Min(w, h) / 3;
        int iconSize = circleR * 2 - 30;
        int iconX = w / 2 - iconSize / 2;
        int iconY = h / 2 - 25 - iconSize / 2;

        return $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {w} {h}' width='{w}' height='{h}'>
  <defs>
    <linearGradient id='bg{Math.Abs(nom.GetHashCode())}' x1='0%' y1='0%' x2='100%' y2='100%'>
      <stop offset='0%'   stop-color='{color1}'/>
      <stop offset='100%' stop-color='{color2}'/>
    </linearGradient>
  </defs>
  <rect width='{w}' height='{h}' fill='url(#bg{Math.Abs(nom.GetHashCode())})'/>
  <circle cx='{w/2}' cy='{h/2 - 25}' r='{circleR}' fill='white' opacity='0.95'/>
  <svg x='{iconX}' y='{iconY}' width='{iconSize}' height='{iconSize}' viewBox='0 0 24 24'>
    <path d='{pathSvg}' fill='none' stroke='{color1}' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>
  </svg>
  <text x='{w/2}' y='{h - 25}' font-family='Arial, sans-serif' font-weight='700' font-size='14' fill='white' text-anchor='middle'>{nomCourt}</text>
</svg>";
    }

    /// <summary>Couleurs + path SVG d'icône inline selon catégorie/nom.</summary>
    private static (string Color1, string Color2, string PathSvg) ConfigPourCategorie(string? cat, string? iconClass, string? nom)
    {
        var s = ((cat ?? "") + " " + (iconClass ?? "") + " " + (nom ?? "")).ToLowerInvariant();

        // Bibliothèque de paths SVG (style Lucide, viewBox 24x24, stroke noir)
        var paths = new System.Collections.Generic.Dictionary<string, string>
        {
            ["laptop"]     = "M3 6h18v10H3z M2 18h20v2H2z",
            ["printer"]    = "M6 4h12v4H6z M4 8h16v8h-3v-2H7v2H4z M7 14h10v6H7z",
            ["desktop"]    = "M3 4h18v12H3z M9 18h6v3H9z M7 21h10v1H7z",
            ["phone"]      = "M7 2h10v20H7z M11 18h2v1h-2z",
            ["tablet"]     = "M5 2h14v20H5z M11 18h2v1h-2z",
            ["camera"]     = "M4 7h3l2-2h6l2 2h3v12H4z M12 16a3 3 0 1 0 0-6 3 3 0 0 0 0 6z",
            ["headphones"] = "M4 14v4c0 1 1 2 2 2h2v-6H4z M16 14v6h2c1 0 2-1 2-2v-4h-4z M4 14a8 8 0 0 1 16 0",
            ["chair"]      = "M6 6h12v6H6z M5 14h14v2H5z M7 16v6 M17 16v6",
            ["couch"]      = "M4 10h16v6H4z M4 16v3 M20 16v3 M6 10V8a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2",
            ["book"]       = "M4 4h14v16H4z M4 20h14 M8 8h6 M8 12h6",
            ["pen"]        = "M14 4l6 6L9 21H3v-6z",
            ["folder"]     = "M2 6h6l2 2h12v12H2z",
            ["box"]        = "M3 7l9-4 9 4v10l-9 4-9-4z M12 12l9-5 M12 12L3 7 M12 12v9",
            ["network"]    = "M5 3h4v4H5z M15 3h4v4h-4z M5 17h4v4H5z M15 17h4v4h-4z M7 7v5h10V7 M12 12v5",
            ["briefcase"]  = "M3 7h18v13H3z M9 7V5h6v2",
            ["calculator"] = "M5 3h14v18H5z M7 7h10v3H7z M7 13h3v2H7z M11 13h2v2h-2z M14 13h3v2h-3z M7 17h3v2H7z M11 17h2v2h-2z M14 17h3v2h-3z",
            ["gift"]       = "M3 8h18v4H3z M5 12h14v10H5z M12 8v14 M8 4a3 3 0 0 1 4 4 M16 4a3 3 0 0 0-4 4",
            ["scissors"]   = "M8 7a3 3 0 1 0 0-4 3 3 0 0 0 0 4z M8 21a3 3 0 1 0 0-4 3 3 0 0 0 0 4z M10 8l11 12 M10 16l11-12",
            ["stapler"]    = "M3 14h18v3H3z M3 14l5-7h10c2 0 3 1 3 3v4",
            ["stamp"]      = "M5 22h14v-2H5z M7 16h10v4H7z M9 12c0-2 0-6 3-6s3 4 3 6 M5 16h14v-2c0-1-1-2-2-2H7c-1 0-2 1-2 2z",
            ["tag"]        = "M3 12l9-9h9v9l-9 9z M16 7a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z",
            ["shield"]     = "M12 2l9 4v6c0 5-4 9-9 10-5-1-9-5-9-10V6z M9 12l2 2 4-4",
            ["coffee"]     = "M3 7h15v9c0 2-2 4-4 4H7c-2 0-4-2-4-4z M18 10h3c1 0 2 1 2 2v2c0 1-1 2-2 2h-3 M6 3v2 M10 3v2 M14 3v2",
            ["palette"]    = "M12 2C7 2 3 6 3 11s4 9 9 9c1 0 2-1 2-2s-1-2 0-3 2-1 3-1c2 0 4-2 4-4 0-5-4-9-9-10z",
            ["truck"]      = "M2 6h12v12H2z M14 10h5l3 3v5h-8z M6 18a2 2 0 1 1 0 4 2 2 0 0 1 0-4z M18 18a2 2 0 1 1 0 4 2 2 0 0 1 0-4z"
        };

        string Choisir(params string[] keys) => paths[keys.FirstOrDefault(k => s.Contains(k)) ?? "box"];

        string path;
        if      (s.Contains("laptop") || s.Contains("portable") || s.Contains("macbook"))           path = paths["laptop"];
        else if (s.Contains("desktop") || (s.Contains("ordinat") && !s.Contains("port")) || s.Contains("pc")) path = paths["desktop"];
        else if (s.Contains("imprim") || s.Contains("print") || s.Contains("photocop") || s.Contains("scanner")) path = paths["printer"];
        else if (s.Contains("smartph") || s.Contains("iphone") || s.Contains("téléph") || s.Contains("mobile")) path = paths["phone"];
        else if (s.Contains("tablet"))                                                              path = paths["tablet"];
        else if (s.Contains("photo") || s.Contains("caméra") || s.Contains("camescope"))           path = paths["camera"];
        else if (s.Contains("casque") || s.Contains("audio") || s.Contains("son") || s.Contains("enceinte")) path = paths["headphones"];
        else if (s.Contains("fauteuil") || s.Contains("siège") || s.Contains("chaise"))            path = paths["chair"];
        else if (s.Contains("canapé") || s.Contains("banquette") || s.Contains("accueil"))         path = paths["couch"];
        else if (s.Contains("cahier") || s.Contains("livre") || s.Contains("agenda") || s.Contains("registre") || s.Contains("bloc")) path = paths["book"];
        else if (s.Contains("stylo") || s.Contains("crayon") || s.Contains("marqueur") || s.Contains("écriture") || s.Contains("surligneur") || s.Contains("correction")) path = paths["pen"];
        else if (s.Contains("classeur") || s.Contains("chemise") || s.Contains("dossier") || s.Contains("classement") || s.Contains("intercalaire") || s.Contains("archive") || s.Contains("trieur")) path = paths["folder"];
        else if (s.Contains("réseau") || s.Contains("router") || s.Contains("switch") || s.Contains("wifi")) path = paths["network"];
        else if (s.Contains("mobilier") || (s.Contains("bureau") && !s.Contains("machine") && !s.Contains("affaire"))) path = paths["briefcase"];
        else if (s.Contains("calcul"))                                                              path = paths["calculator"];
        else if (s.Contains("cadeau") || s.Contains("promot") || s.Contains("destockage"))         path = paths["gift"];
        else if (s.Contains("ciseaux") || s.Contains("découpe") || s.Contains("cutter"))           path = paths["scissors"];
        else if (s.Contains("agraf") || s.Contains("perfor") || s.Contains("trombone") || s.Contains("attache")) path = paths["stapler"];
        else if (s.Contains("cachet") || s.Contains("tampon") || s.Contains("dateur"))             path = paths["stamp"];
        else if (s.Contains("papier") || s.Contains("étiquette") || s.Contains("courrier") || s.Contains("enveloppe")) path = paths["tag"];
        else if (s.Contains("sécurité") || s.Contains("coffre") || s.Contains("extincteur") || s.Contains("surveillance")) path = paths["shield"];
        else if (s.Contains("café") || s.Contains("expresso") || s.Contains("cafetière") || s.Contains("boisson") || s.Contains("alimentation")) path = paths["coffee"];
        else if (s.Contains("coloriage") || s.Contains("peinture") || s.Contains("gouache"))       path = paths["palette"];
        else if (s.Contains("emballage") || s.Contains("manutention") || s.Contains("livraison"))  path = paths["truck"];
        else                                                                                       path = paths["box"];

        // Couleurs par catégorie
        var (c1, c2) = s switch
        {
            var x when x.Contains("informatique") || x.Contains("ordinat") || x.Contains("laptop")  => ("#0066ff", "#00d9ff"),
            var x when x.Contains("imprim") || x.Contains("consommable") || x.Contains("toner")     => ("#f59e0b", "#dc3545"),
            var x when x.Contains("mobilier") || x.Contains("fauteuil")                             => ("#8b4513", "#d2691e"),
            var x when x.Contains("papier") || x.Contains("papeterie") || x.Contains("courrier")    => ("#0ea5e9", "#06b6d4"),
            var x when x.Contains("écriture") || x.Contains("stylo") || x.Contains("crayon") || x.Contains("marqueur") => ("#7c3aed", "#ec4899"),
            var x when x.Contains("petite fourniture") || x.Contains("agraf") || x.Contains("perfor") || x.Contains("attache") => ("#f97316", "#facc15"),
            var x when x.Contains("classement") || x.Contains("classeur") || x.Contains("archive")  => ("#0d9488", "#22c55e"),
            var x when x.Contains("présentation") || x.Contains("tableau") || x.Contains("vidéoproj") => ("#dc2626", "#f97316"),
            var x when x.Contains("scolaire") || x.Contains("sac") || x.Contains("coloriage")       => ("#a855f7", "#ec4899"),
            var x when x.Contains("service") || x.Contains("hygiène") || x.Contains("entretien")    => ("#10b981", "#06b6d4"),
            var x when x.Contains("mobile") || x.Contains("téléph")                                  => ("#6366f1", "#a855f7"),
            var x when x.Contains("photo") || x.Contains("audio") || x.Contains("vidéo")            => ("#ef4444", "#f59e0b"),
            var x when x.Contains("cadeau") || x.Contains("promot")                                  => ("#ec4899", "#f43f5e"),
            var x when x.Contains("machine") || x.Contains("cachet") || x.Contains("accessoire")    => ("#0ea5e9", "#6366f1"),
            _ => ("#0d6efd", "#6610f2")
        };

        return (c1, c2, path);
    }

    /// <summary>SVG produit professionnel avec icône path inline + nom + dégradé.</summary>
    private static string GenererSvg(string nom, string? marque, string color1, string color2, string pathSvg, int w, int h)
    {
        var nomCourt = nom ?? "Produit";
        if (nomCourt.Length > 32) nomCourt = nomCourt.Substring(0, 30) + "…";
        nomCourt = System.Net.WebUtility.HtmlEncode(nomCourt);

        var marqueAffichee = string.IsNullOrWhiteSpace(marque) ? "SODIV" : System.Net.WebUtility.HtmlEncode(marque);

        // Bulles décoratives en arrière-plan (graine STABLE → rendu reproductible
        // entre deux démarrages, indispensable pour la recherche par image).
        var seed = SvgRasterizer.HashStable(nom);
        var rng = new Random(seed);
        var bubbles = "";
        for (int i = 0; i < 4; i++)
        {
            var cx = rng.Next(0, w);
            var cy = rng.Next(0, h);
            var r  = rng.Next(40, 120);
            var opacity = (rng.Next(8, 18) / 100.0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            bubbles += $"<circle cx='{cx}' cy='{cy}' r='{r}' fill='white' opacity='{opacity}'/>";
        }

        // L'icône fait viewBox 0 0 24 24. On la centre dans le cercle blanc.
        // Position du cercle blanc : centre (w/2, h/2 - 30), rayon ~ min(w,h)/5
        int circleR = Math.Min(w, h) / 5;
        int iconSize = circleR * 2 - 30; // l'icône tient dans le cercle avec un peu de marge
        int iconX = w / 2 - iconSize / 2;
        int iconY = h / 2 - 30 - iconSize / 2;

        return $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {w} {h}' width='{w}' height='{h}'>
  <defs>
    <linearGradient id='bg' x1='0%' y1='0%' x2='100%' y2='100%'>
      <stop offset='0%'   stop-color='{color1}'/>
      <stop offset='100%' stop-color='{color2}'/>
    </linearGradient>
    <linearGradient id='ic' x1='0%' y1='0%' x2='100%' y2='100%'>
      <stop offset='0%'   stop-color='{color1}'/>
      <stop offset='100%' stop-color='{color2}'/>
    </linearGradient>
  </defs>
  <rect width='{w}' height='{h}' fill='url(#bg)'/>
  {bubbles}
  <circle cx='{w/2}' cy='{h/2 - 30}' r='{circleR}' fill='white' opacity='0.97'/>
  <svg x='{iconX}' y='{iconY}' width='{iconSize}' height='{iconSize}' viewBox='0 0 24 24'>
    <path d='{pathSvg}' fill='none' stroke='url(#ic)' stroke-width='1.8' stroke-linecap='round' stroke-linejoin='round'/>
  </svg>
  <text x='{w/2}' y='{h - 80}' font-family='Arial, sans-serif' font-weight='bold' font-size='14' fill='white' opacity='0.85' text-anchor='middle' letter-spacing='2'>{marqueAffichee.ToUpper()}</text>
  <text x='{w/2}' y='{h - 50}' font-family='Arial, sans-serif' font-weight='600' font-size='18' fill='white' text-anchor='middle'>{nomCourt}</text>
  <text x='{w/2}' y='{h - 20}' font-family='Arial, sans-serif' font-size='11' fill='white' opacity='0.6' text-anchor='middle' letter-spacing='3'>SODIV BUREAU</text>
</svg>";
    }
}
