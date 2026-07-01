using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public record StatsEntonnoir(int Visiteurs, int VueProduit, int AjoutPanier, int Checkout, int Achats);

public record StatTopProduit(int ProduitId, string Nom, int NbVues);
public record StatRepartition(string Cle, int Nb);

public interface ITrackingService
{
    Task EnregistrerAsync(TypeEvenement type, HttpContext ctx,
        string? cibleType = null, int? cibleId = null,
        string? etiquette = null, decimal? valeur = null,
        int? dureeMs = null, string? metadonnees = null);

    Task<StatsEntonnoir> EntonnoirAsync(DateTime depuis);
    Task<List<StatTopProduit>> TopProduitsVusAsync(DateTime depuis, int max = 10);
    Task<List<StatRepartition>> RepartitionAppareilsAsync(DateTime depuis);
    Task<Dictionary<string,int>> EvenementsParJourAsync(DateTime depuis);
    Task<List<EvenementComportement>> RecentsAsync(int max = 50);
    Task<int> CompterAsync(TypeEvenement t, DateTime depuis);
    TypeAppareil DetecterAppareil(string? userAgent);
}

public class TrackingService : ITrackingService
{
    private readonly AppDbContext _db;
    public TrackingService(AppDbContext db) { _db = db; }

    public TypeAppareil DetecterAppareil(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return TypeAppareil.Inconnu;
        if (Regex.IsMatch(ua, @"(bot|crawl|spider|curl|wget|python|java/)", RegexOptions.IgnoreCase))
            return TypeAppareil.Bot;
        if (Regex.IsMatch(ua, @"(iPad|Tablet|PlayBook|Kindle|Nexus 7|Nexus 10)", RegexOptions.IgnoreCase))
            return TypeAppareil.Tablette;
        if (Regex.IsMatch(ua, @"(Mobi|Android|iPhone|iPod|BlackBerry|IEMobile|Windows Phone)", RegexOptions.IgnoreCase))
            return TypeAppareil.Mobile;
        return TypeAppareil.Ordinateur;
    }

    public async Task EnregistrerAsync(TypeEvenement type, HttpContext ctx,
        string? cibleType = null, int? cibleId = null,
        string? etiquette = null, decimal? valeur = null,
        int? dureeMs = null, string? metadonnees = null)
    {
        try
        {
            var ua = ctx.Request.Headers.UserAgent.ToString();
            var ev = new EvenementComportement
            {
                UtilisateurId = ctx.Session.GetInt32("UtilisateurId"),
                SessionId     = ctx.Session.Id,
                Type          = type,
                CibleType     = cibleType,
                CibleId       = cibleId,
                Page          = ctx.Request.Path.Value,
                Referer       = ctx.Request.Headers.Referer.ToString().NullSi(""),
                UserAgent     = ua.Length > 500 ? ua[..500] : ua,
                IpAdresse     = ctx.Connection.RemoteIpAddress?.ToString(),
                Appareil      = DetecterAppareil(ua),
                Etiquette     = etiquette,
                Valeur        = valeur,
                DureeMs       = dureeMs,
                Metadonnees   = metadonnees
            };
            _db.EvenementsComportement.Add(ev);
            await _db.SaveChangesAsync();
        }
        catch
        {
            // Tracking ne doit jamais casser la navigation
        }
    }

    public async Task<StatsEntonnoir> EntonnoirAsync(DateTime depuis)
    {
        // Visiteurs uniques (par session) avec au moins 1 PageVue
        var visiteurs = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.PageVue)
            .Select(e => e.SessionId).Distinct().CountAsync();

        var vueProduit = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.ProduitVu)
            .Select(e => e.SessionId).Distinct().CountAsync();

        var ajoutPanier = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.AjoutPanier)
            .Select(e => e.SessionId).Distinct().CountAsync();

        var checkout = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.CheckoutDemarre)
            .Select(e => e.SessionId).Distinct().CountAsync();

        var achats = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.CommandePassee)
            .Select(e => e.SessionId).Distinct().CountAsync();

        return new StatsEntonnoir(visiteurs, vueProduit, ajoutPanier, checkout, achats);
    }

    public async Task<List<StatTopProduit>> TopProduitsVusAsync(DateTime depuis, int max = 10)
    {
        var ids = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.ProduitVu
                        && e.CibleType == "Produit" && e.CibleId != null)
            .GroupBy(e => e.CibleId!.Value)
            .Select(g => new { ProduitId = g.Key, NbVues = g.Count() })
            .OrderByDescending(x => x.NbVues)
            .Take(max)
            .ToListAsync();

        if (!ids.Any()) return new();

        var idsList = ids.Select(i => i.ProduitId).ToList();
        var produits = await _db.Produits
            .Where(p => idsList.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nom);

        return ids.Select(i => new StatTopProduit(
            i.ProduitId,
            produits.TryGetValue(i.ProduitId, out var nom) ? nom : $"#{i.ProduitId}",
            i.NbVues
        )).ToList();
    }

    public async Task<List<StatRepartition>> RepartitionAppareilsAsync(DateTime depuis)
    {
        var brut = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis && e.Type == TypeEvenement.PageVue)
            .GroupBy(e => e.Appareil)
            .Select(g => new { Appareil = g.Key, Nb = g.Count() })
            .ToListAsync();

        return brut
            .Select(x => new StatRepartition(x.Appareil.ToString(), x.Nb))
            .OrderByDescending(s => s.Nb)
            .ToList();
    }

    public async Task<Dictionary<string,int>> EvenementsParJourAsync(DateTime depuis)
    {
        var brut = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis)
            .GroupBy(e => e.Date.Date)
            .Select(g => new { Date = g.Key, Nb = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();
        return brut.ToDictionary(x => x.Date.ToString("dd/MM"), x => x.Nb);
    }

    public async Task<List<EvenementComportement>> RecentsAsync(int max = 50) =>
        await _db.EvenementsComportement.OrderByDescending(e => e.Date).Take(max).ToListAsync();

    public async Task<int> CompterAsync(TypeEvenement t, DateTime depuis) =>
        await _db.EvenementsComportement.CountAsync(e => e.Type == t && e.Date >= depuis);
}

internal static class StringExt
{
    public static string? NullSi(this string s, string vide) => s == vide ? null : s;
}
