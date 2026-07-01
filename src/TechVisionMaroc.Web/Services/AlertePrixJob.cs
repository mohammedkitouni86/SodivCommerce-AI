using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using TechVisionMaroc.Data;

namespace TechVisionMaroc.Services;

/// <summary>
/// Job en arrière-plan qui vérifie toutes les 6 heures les baisses de prix sur les wishlists
/// et envoie un email aux utilisateurs ayant activé l'alerte (seuil : -5%).
/// </summary>
public class AlertePrixJob : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<AlertePrixJob> _log;

    // Seuil de baisse pour déclencher l'email (5%).
    private const decimal SeuilBaisse = 0.05m;
    // Anti-spam : on n'envoie au max 1 email par produit/utilisateur tous les 7 jours.
    private static readonly TimeSpan FrequenceMaxParProduit = TimeSpan.FromDays(7);
    // Cadence du job.
    private static readonly TimeSpan Cadence = TimeSpan.FromHours(6);

    public AlertePrixJob(IServiceProvider sp, IConfiguration config, ILogger<AlertePrixJob> log)
    {
        _sp = sp; _config = config; _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Premier passage : attendre 1 min après démarrage
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await VerifierAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "AlertePrixJob — erreur"); }

            try { await Task.Delay(Cadence, stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    private async Task VerifierAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var maintenant = DateTime.UtcNow;

        var candidats = await db.Wishlists
            .Where(w => w.AlertePrix)
            .Include(w => w.Produit)
            .Include(w => w.Utilisateur)
            .ToListAsync(ct);

        int envoyes = 0;

        foreach (var w in candidats)
        {
            if (w.Produit == null || w.Utilisateur == null) continue;
            if (w.DerniereAlerte.HasValue && (maintenant - w.DerniereAlerte.Value) < FrequenceMaxParProduit) continue;

            var prixActuel = w.Produit.PrixPromo ?? w.Produit.Prix;
            if (w.PrixReference <= 0) continue;

            var baisseRatio = (w.PrixReference - prixActuel) / w.PrixReference;
            if (baisseRatio < SeuilBaisse) continue;

            try
            {
                EnvoyerEmail(
                    w.Utilisateur.Email,
                    string.IsNullOrWhiteSpace(w.Utilisateur.Nom) ? w.Utilisateur.Email : w.Utilisateur.Nom,
                    w.Produit.Nom,
                    w.Produit.Id,
                    w.PrixReference,
                    prixActuel,
                    baisseRatio
                );
                w.DerniereAlerte = maintenant;
                envoyes++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AlertePrixJob — envoi email échoué pour {Email}", w.Utilisateur.Email);
            }
        }

        if (envoyes > 0)
        {
            await db.SaveChangesAsync(ct);
            _log.LogInformation("AlertePrixJob — {N} email(s) d'alerte envoyé(s).", envoyes);
        }
    }

    private void EnvoyerEmail(string destinataire, string nomDestinataire, string nomProduit,
                              int produitId, decimal prixRef, decimal prixActuel, decimal baisseRatio)
    {
        var smtpHost  = _config["Email:SmtpHost"];
        var smtpPort  = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var adresse   = _config["Email:AdresseGmail"];
        var motPasse  = _config["Email:MotDePasseApp"];
        var expediteur= _config["Email:Expediteur"] ?? adresse;

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(adresse) || string.IsNullOrWhiteSpace(motPasse))
        {
            _log.LogWarning("AlertePrixJob — configuration SMTP incomplète, email ignoré.");
            return;
        }

        var economie  = prixRef - prixActuel;
        var pourcent  = (int)Math.Round(baisseRatio * 100);

        var corps = $@"
<!DOCTYPE html><html><body style='font-family:Segoe UI,Arial,sans-serif;background:#f5f6f8;padding:20px'>
  <div style='max-width:560px;margin:auto;background:#fff;border-radius:8px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.06)'>
    <h2 style='color:#0d6efd;margin-top:0'>💰 Baisse de prix sur votre wishlist !</h2>
    <p>Bonjour <strong>{WebUtility.HtmlEncode(nomDestinataire)}</strong>,</p>
    <p>Bonne nouvelle : le prix du produit suivant a <strong>baissé de {pourcent}%</strong> :</p>
    <div style='background:#f8f9fa;padding:16px;border-radius:6px;margin:16px 0'>
      <div style='font-size:18px;font-weight:600'>{WebUtility.HtmlEncode(nomProduit)}</div>
      <div style='color:#6c757d;text-decoration:line-through'>Ancien prix : {prixRef:N2} MAD</div>
      <div style='color:#0d6efd;font-size:22px;font-weight:700'>Nouveau prix : {prixActuel:N2} MAD</div>
      <div style='color:#198754;font-weight:600;margin-top:4px'>Vous économisez {economie:N2} MAD</div>
    </div>
    <a href='https://sodibureau.ma/Produit/Details/{produitId}'
       style='display:inline-block;background:#0d6efd;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;font-weight:600'>
      Voir le produit
    </a>
    <p style='color:#6c757d;font-size:12px;margin-top:24px'>
      Vous recevez cet email car vous avez activé l'alerte de baisse de prix dans votre liste de souhaits.
      Pour désactiver cette alerte, rendez-vous sur <a href='https://sodibureau.ma/wishlist'>votre wishlist</a>.
    </p>
  </div>
</body></html>";

        using var smtp = new SmtpClient(smtpHost, smtpPort)
        {
            Credentials    = new NetworkCredential(adresse, motPasse),
            EnableSsl      = true,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        using var mail = new MailMessage
        {
            From       = new MailAddress(adresse, "SODIV Bureau"),
            Subject    = $"💰 Baisse de prix : {nomProduit} (-{pourcent}%)",
            Body       = corps,
            IsBodyHtml = true
        };
        mail.To.Add(destinataire);
        smtp.Send(mail);
    }
}
