using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public record EvenementSuivi(DateTime Date, string Statut, string Lieu, string Description, string Icone);

public record InfoTransporteur(string Nom, string LogoIcone, string Couleur, string TelSupport, string SiteWeb);

public interface ISuiviColisService
{
    InfoTransporteur Info(Transporteur t);
    string? UrlSuivi(Transporteur t, string? numero);
    IReadOnlyList<EvenementSuivi> GenererTimeline(Commande commande);
    Task<Commande?> ObtenirParNumeroAsync(string numeroCommande, string? numeroSuivi = null);
    Task DefinirSuiviAsync(int commandeId, Transporteur t, string? numeroSuivi);
    string GenererNumeroAutomatique(Transporteur t);
}

public class SuiviColisService : ISuiviColisService
{
    private readonly AppDbContext _db;
    public SuiviColisService(AppDbContext db) { _db = db; }

    public InfoTransporteur Info(Transporteur t) => t switch
    {
        Transporteur.Amana       => new("Amana (Poste Maroc)", "fa-mail-bulk",   "danger",  "+212 5 37 71 89 00", "https://www.bam.poste.ma"),
        Transporteur.CTMExpress  => new("CTM Express",          "fa-bus",         "warning", "+212 5 22 54 10 10", "https://www.ctm.ma"),
        Transporteur.DHL         => new("DHL Express",          "fa-plane",       "warning", "+212 5 22 97 20 20", "https://www.dhl.com/ma"),
        Transporteur.Autre       => new("Autre transporteur",   "fa-truck",       "secondary","",                  ""),
        _                        => new("Non assigné",          "fa-question",    "muted",   "",                  ""),
    };

    public string? UrlSuivi(Transporteur t, string? numero)
    {
        if (string.IsNullOrWhiteSpace(numero)) return null;
        var n = Uri.EscapeDataString(numero.Trim());
        return t switch
        {
            Transporteur.Amana      => $"https://www.bam.poste.ma/fr/track-trace?id={n}",
            Transporteur.CTMExpress => $"https://www.ctm.ma/messagerie/suivi-de-colis?numero={n}",
            Transporteur.DHL        => $"https://www.dhl.com/ma-fr/home/tracking/tracking-express.html?tracking-id={n}",
            _ => null
        };
    }

    public string GenererNumeroAutomatique(Transporteur t)
    {
        var rand = Random.Shared.NextInt64(1_000_000, 9_999_999);
        return t switch
        {
            Transporteur.Amana      => $"AM{DateTime.UtcNow:yyMM}{rand}MA",
            Transporteur.CTMExpress => $"CTM-{DateTime.UtcNow:yyMMdd}-{rand}",
            Transporteur.DHL        => $"DHL{rand}WW",
            _                       => $"TVM-{rand}"
        };
    }

    /// <summary>
    /// Génère une timeline d'événements à partir du statut de la commande
    /// et de la date d'expédition. Pas de stockage en base : événements simulés
    /// à la volée (suffisant pour 99% des cas e-commerce Maroc).
    /// </summary>
    public IReadOnlyList<EvenementSuivi> GenererTimeline(Commande c)
    {
        var ev = new List<EvenementSuivi>();
        var lieuDepart = "Centre logistique SODIV Bureau — Salé";
        var lieuArrivee = $"{c.VilleLivraison} — Maroc";

        // 1. Commande passée
        ev.Add(new EvenementSuivi(
            c.DateCommande,
            "Commande enregistrée",
            "Plateforme TechVisionMaroc",
            $"Commande {c.NumeroCommande} créée et en attente de validation.",
            "fa-clipboard-check"
        ));

        if (c.Statut == StatutCommande.Annulee || c.Statut == StatutCommande.Remboursee)
        {
            ev.Add(new EvenementSuivi(
                c.DateCommande.AddHours(1),
                c.Statut == StatutCommande.Annulee ? "Commande annulée" : "Commande remboursée",
                "Service client TechVisionMaroc",
                "Traitement interrompu.",
                "fa-times-circle"
            ));
            return ev;
        }

        // 2. Confirmation
        if (c.Statut >= StatutCommande.Confirmee)
        {
            ev.Add(new EvenementSuivi(
                c.DateCommande.AddHours(2),
                "Paiement confirmé",
                "Service comptable",
                "La commande a été validée et transmise à la préparation.",
                "fa-check-circle"
            ));
        }

        // 3. Préparation
        if (c.Statut >= StatutCommande.EnPreparation)
        {
            ev.Add(new EvenementSuivi(
                c.DateCommande.AddHours(6),
                "Préparation du colis",
                lieuDepart,
                "Vos articles sont en cours d'emballage dans notre entrepôt.",
                "fa-box"
            ));
        }

        // 4. Expédition
        var dateExp = c.DateExpedition ?? c.DateCommande.AddDays(1);
        if (c.Statut >= StatutCommande.Expediee)
        {
            var info = Info(c.Transporteur);
            ev.Add(new EvenementSuivi(
                dateExp,
                "Colis expédié",
                lieuDepart,
                string.IsNullOrEmpty(c.NumeroSuivi)
                    ? $"Pris en charge par {info.Nom}."
                    : $"Pris en charge par {info.Nom} — N° {c.NumeroSuivi}",
                "fa-truck"
            ));

            // Transit (uniquement si pas encore livré)
            if (c.Statut != StatutCommande.Livree)
            {
                ev.Add(new EvenementSuivi(
                    dateExp.AddHours(12),
                    "En transit",
                    "Plateforme de tri régional",
                    $"Le colis est en cours d'acheminement vers {c.VilleLivraison}.",
                    "fa-route"
                ));
            }
        }

        // 5. Livraison
        if (c.Statut == StatutCommande.Livree)
        {
            var dateLiv = c.DateLivraison ?? dateExp.AddDays(2);
            ev.Add(new EvenementSuivi(
                dateLiv.AddHours(-3),
                "En cours de livraison",
                lieuArrivee,
                "Le livreur est en route vers votre adresse.",
                "fa-motorcycle"
            ));
            ev.Add(new EvenementSuivi(
                dateLiv,
                "Colis livré",
                lieuArrivee,
                "Votre colis a été remis avec succès. Merci de votre confiance !",
                "fa-box-open"
            ));
        }

        return ev.OrderByDescending(e => e.Date).ToList();
    }

    public async Task<Commande?> ObtenirParNumeroAsync(string numeroCommande, string? numeroSuivi = null)
    {
        var q = _db.Commandes
            .Include(c => c.Lignes).ThenInclude(l => l.Produit)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(numeroSuivi))
            return await q.FirstOrDefaultAsync(c =>
                c.NumeroCommande == numeroCommande && c.NumeroSuivi == numeroSuivi);

        return await q.FirstOrDefaultAsync(c => c.NumeroCommande == numeroCommande);
    }

    public async Task DefinirSuiviAsync(int commandeId, Transporteur t, string? numeroSuivi)
    {
        var c = await _db.Commandes.FindAsync(commandeId);
        if (c == null) return;
        c.Transporteur = t;
        c.NumeroSuivi = string.IsNullOrWhiteSpace(numeroSuivi) ? null : numeroSuivi.Trim();
        // Marque la date d'expédition la première fois qu'un transporteur est défini
        if (t != Transporteur.Aucun && c.DateExpedition == null)
            c.DateExpedition = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
