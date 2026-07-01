using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml;
using TechVisionMaroc.Data;

namespace TechVisionMaroc.Controllers;

public class SeoController : Controller
{
    private readonly AppDbContext _db;
    public SeoController(AppDbContext db) => _db = db;

    [Route("/robots.txt")]
    [ResponseCache(Duration = 86400)]
    public IActionResult Robots()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var sb = new StringBuilder();
        sb.AppendLine("User-agent: *");
        sb.AppendLine("Allow: /");
        sb.AppendLine("Disallow: /admin");
        sb.AppendLine("Disallow: /Account");
        sb.AppendLine("Disallow: /Panier");
        sb.AppendLine("Disallow: /Commande");
        sb.AppendLine("Disallow: /api/");
        sb.AppendLine();
        sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");
        return Content(sb.ToString(), "text/plain", Encoding.UTF8);
    }

    [Route("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        await using var ms = new MemoryStream();
        var settings = new XmlWriterSettings { Async = true, Encoding = Encoding.UTF8, Indent = true };
        await using (var w = XmlWriter.Create(ms, settings))
        {
            await w.WriteStartDocumentAsync();
            await w.WriteStartElementAsync(null, "urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            async Task EcrireUrl(string loc, DateTime lastmod, string changefreq, string priority)
            {
                await w.WriteStartElementAsync(null, "url", null);
                await w.WriteElementStringAsync(null, "loc", null, loc);
                await w.WriteElementStringAsync(null, "lastmod", null, lastmod.ToString("yyyy-MM-dd"));
                await w.WriteElementStringAsync(null, "changefreq", null, changefreq);
                await w.WriteElementStringAsync(null, "priority", null, priority);
                await w.WriteEndElementAsync();
            }

            await EcrireUrl(baseUrl + "/",          DateTime.UtcNow, "daily",   "1.0");
            await EcrireUrl(baseUrl + "/Catalogue", DateTime.UtcNow, "daily",   "0.9");
            await EcrireUrl(baseUrl + "/Home/APropos", DateTime.UtcNow, "monthly", "0.5");
            await EcrireUrl(baseUrl + "/Home/Contact", DateTime.UtcNow, "monthly", "0.5");

            var categories = await _db.Categories.Where(c => c.EstActive).Select(c => new { c.Id, c.Nom }).ToListAsync();
            foreach (var c in categories)
                await EcrireUrl($"{baseUrl}/Catalogue?categorieId={c.Id}", DateTime.UtcNow, "weekly", "0.7");

            var produits = await _db.Produits
                .Where(p => p.EstActif)
                .Select(p => new { p.Id, DateModification = p.DateCreation })
                .ToListAsync();
            foreach (var p in produits)
                await EcrireUrl($"{baseUrl}/Produit/Details/{p.Id}", p.DateModification, "weekly", "0.8");

            await w.WriteEndElementAsync();
            await w.WriteEndDocumentAsync();
        }
        return File(ms.ToArray(), "application/xml; charset=utf-8");
    }
}
