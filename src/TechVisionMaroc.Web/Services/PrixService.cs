using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace TechVisionMaroc.Services;

public interface IPrixService
{
    /// <summary>true si l'utilisateur préfère voir les prix HT (entreprises B2B).</summary>
    bool ModeHT { get; }

    /// <summary>Calcule le prix HT à partir d'un prix TTC et d'un taux de TVA (ex 20).</summary>
    decimal Ht(decimal ttc, decimal tauxTva);

    /// <summary>Montant de TVA correspondant à un prix TTC.</summary>
    decimal Tva(decimal ttc, decimal tauxTva);

    /// <summary>Renvoie le prix à afficher selon le mode courant (HT ou TTC) — pour les listings.</summary>
    decimal AfficherSelonMode(decimal ttc, decimal tauxTva);

    /// <summary>Formate un montant avec le suffixe MAD + libellé HT/TTC selon le mode courant.</summary>
    string Format(decimal ttc, decimal tauxTva, bool montrerLibelle = true);

    /// <summary>Libellé court : "HT" ou "TTC" selon le mode courant.</summary>
    string LibelleMode { get; }
}

public class PrixService : IPrixService
{
    public const string CookieName = "prix_mode";
    private readonly IHttpContextAccessor _ctx;

    public PrixService(IHttpContextAccessor ctx) => _ctx = ctx;

    public bool ModeHT
    {
        get
        {
            var v = _ctx.HttpContext?.Request.Cookies[CookieName];
            return string.Equals(v, "HT", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string LibelleMode => "";

    public decimal Ht(decimal ttc, decimal tauxTva)
        => Math.Round(ttc / (1m + (tauxTva / 100m)), 2, MidpointRounding.AwayFromZero);

    public decimal Tva(decimal ttc, decimal tauxTva)
        => Math.Round(ttc - Ht(ttc, tauxTva), 2, MidpointRounding.AwayFromZero);

    public decimal AfficherSelonMode(decimal ttc, decimal tauxTva)
        => ModeHT ? Ht(ttc, tauxTva) : ttc;

    public string Format(decimal ttc, decimal tauxTva, bool montrerLibelle = true)
    {
        var montant = AfficherSelonMode(ttc, tauxTva);
        return montant.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " MAD";
    }
}
