using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

/// <summary>
/// Job marketing en arrière-plan : détecte les clients qui n'ont plus acheté
/// depuis 30 jours et leur envoie automatiquement un email de relance contenant
/// des produits EN PROMOTION de leur catégorie préférée (déduite de leur
/// historique d'achats). Anti-spam : une seule relance par client tous les 30 jours
/// (traçée dans AuditLogs, Action = "RelanceInactivite").
/// </summary>
public class RelanceInactiviteJob : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _config;
    private readonly ILogger<RelanceInactiviteJob> _log;

    // Inactivité déclenchant la relance.
    private static readonly TimeSpan SeuilInactivite = TimeSpan.FromDays(30);
    // Anti-spam : pas plus d'une relance par client sur cette période.
    private static readonly TimeSpan FrequenceMaxParClient = TimeSpan.FromDays(30);
    // Cadence de vérification du job.
    private static readonly TimeSpan Cadence = TimeSpan.FromHours(24);
    private const string ActionAudit = "RelanceInactivite";

    private readonly IHostEnvironment _env;

    public RelanceInactiviteJob(IServiceProvider sp, IConfiguration config, ILogger<RelanceInactiviteJob> log, IHostEnvironment env)
    {
        _sp = sp; _config = config; _log = log; _env = env;
    }

    /// <summary>
    /// En développement (clients de démo aux emails fictifs), le job fonctionne en
    /// MODE SIMULATION : il calcule les relances et les trace dans le journal d'audit,
    /// mais n'envoie aucun email réel. En production, l'envoi SMTP est effectif.
    /// </summary>
    private bool ModeSimulation => _env.IsDevelopment();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Laisser l'application démarrer (seed, schéma) avant le premier passage.
        await Task.Delay(ModeSimulation ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RelancerAsync(stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "RelanceInactiviteJob — erreur"); }

            try { await Task.Delay(Cadence, stoppingToken); } catch (OperationCanceledException) { }
        }
    }

    private async Task RelancerAsync(CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var maintenant = DateTime.UtcNow;
        var limiteInactivite = maintenant - SeuilInactivite;
        var limiteAntiSpam = maintenant - FrequenceMaxParClient;

        // 1) Clients actifs dont la DERNIÈRE commande (non annulée) date de plus de 30 jours.
        var derniersAchats = await db.Commandes
            .Where(c => c.Statut != StatutCommande.Annulee && c.Statut != StatutCommande.Remboursee)
            .GroupBy(c => c.UtilisateurId)
            .Select(g => new { UtilisateurId = g.Key, Derniere = g.Max(c => c.DateCommande) })
            .Where(x => x.Derniere < limiteInactivite)
            .ToListAsync(ct);

        if (derniersAchats.Count == 0) return;

        // 2) Clients déjà relancés récemment (anti-spam).
        var dejaRelances = await db.AuditLogs
            .Where(a => a.Action == ActionAudit && a.Date >= limiteAntiSpam && a.UtilisateurId != null)
            .Select(a => a.UtilisateurId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var cibles = derniersAchats
            .Where(x => !dejaRelances.Contains(x.UtilisateurId))
            .ToList();

        // En simulation (démo locale), limiter à 5 relances par passage pour garder
        // un journal d'audit lisible devant le jury.
        int maxParPassage = ModeSimulation ? 5 : int.MaxValue;
        int envoyes = 0;

        foreach (var cible in cibles)
        {
            if (envoyes >= maxParPassage) break;
            var client = await db.Utilisateurs.FindAsync(new object[] { cible.UtilisateurId }, ct);
            if (client == null || !client.EstActif || client.Role != "Client") continue;
            if (string.IsNullOrWhiteSpace(client.Email)) continue;

            // 3) Catégorie préférée : celle où le client a acheté le plus d'articles.
            var categoriePreferee = await db.LignesCommande
                .Where(l => l.Commande!.UtilisateurId == client.Id
                         && l.Commande.Statut != StatutCommande.Annulee
                         && l.Produit != null)
                .GroupBy(l => l.Produit!.CategorieId)
                .Select(g => new { CategorieId = g.Key, Quantite = g.Sum(l => l.Quantite) })
                .OrderByDescending(x => x.Quantite)
                .FirstOrDefaultAsync(ct);

            if (categoriePreferee == null) continue;

            var categorie = await db.Categories.FindAsync(new object[] { categoriePreferee.CategorieId }, ct);
            if (categorie == null) continue;

            // 4) Jusqu'à 3 produits EN PROMOTION dans cette catégorie
            //    (à défaut : meilleures promos toutes catégories confondues).
            var promos = await db.Produits
                .Where(p => p.EstActif && p.PrixPromo != null && p.PrixPromo < p.Prix
                         && p.CategorieId == categorie.Id)
                .OrderByDescending(p => (p.Prix - p.PrixPromo!.Value) / p.Prix)
                .Take(3)
                .ToListAsync(ct);

            if (promos.Count == 0)
            {
                promos = await db.Produits
                    .Where(p => p.EstActif && p.PrixPromo != null && p.PrixPromo < p.Prix)
                    .OrderByDescending(p => (p.Prix - p.PrixPromo!.Value) / p.Prix)
                    .Take(3)
                    .ToListAsync(ct);
            }
            if (promos.Count == 0) continue; // aucune promo disponible sur tout le site

            try
            {
                // En simulation (développement) : pas d'envoi SMTP réel vers les
                // emails fictifs de démo — seule la trace d'audit est créée.
                if (!ModeSimulation)
                {
                    EnvoyerEmail(client.Email,
                        string.IsNullOrWhiteSpace(client.Prenom) ? client.Email : client.Prenom,
                        categorie.Nom, promos);
                }

                // 5) Tracer l'envoi (sert aussi d'anti-spam au prochain passage).
                db.AuditLogs.Add(new AuditLog
                {
                    UtilisateurId = client.Id,
                    UtilisateurEmail = client.Email,
                    Role = client.Role,
                    Action = ActionAudit,
                    Cible = $"Categorie:{categorie.Id}",
                    Details = $"{(ModeSimulation ? "[SIMULATION] " : "")}Relance après {(maintenant - cible.Derniere).Days} j d'inactivité — {promos.Count} promo(s) « {categorie.Nom} »",
                    Date = maintenant
                });
                await db.SaveChangesAsync(ct);
                envoyes++;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "RelanceInactivite — échec d'envoi à {Email}", client.Email);
            }
        }

        if (envoyes > 0)
            _log.LogInformation("RelanceInactivite — {Nb} relance(s) {Mode}.", envoyes,
                ModeSimulation ? "simulée(s) [audit uniquement]" : "envoyée(s) par email");
    }

    private void EnvoyerEmail(string destinataire, string prenom, string nomCategorie, List<Produit> promos)
    {
        var smtpHost   = _config["Email:SmtpHost"];
        var smtpPort   = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var adresse    = _config["Email:AdresseGmail"];
        var motPasse   = _config["Email:MotDePasseApp"];

        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(adresse) || string.IsNullOrWhiteSpace(motPasse))
        {
            _log.LogWarning("RelanceInactivite — configuration SMTP incomplète, email ignoré.");
            return;
        }

        var blocsProduits = string.Join("", promos.Select(pr =>
        {
            var promo = pr.PrixPromo!.Value;
            var pourcent = (int)Math.Round((pr.Prix - promo) / pr.Prix * 100);
            return $@"
    <div style='background:#f8f9fa;padding:14px;border-radius:6px;margin:12px 0'>
      <div style='font-size:16px;font-weight:600'>{WebUtility.HtmlEncode(pr.Nom)}
        <span style='background:#dc3545;color:#fff;border-radius:4px;padding:2px 8px;font-size:12px;margin-left:8px'>-{pourcent}%</span>
      </div>
      <div style='color:#6c757d;text-decoration:line-through'>{pr.Prix:N2} MAD</div>
      <div style='color:#0d6efd;font-size:20px;font-weight:700'>{promo:N2} MAD</div>
      <a href='https://sodiv1bureau.runasp.net/Produit/Details/{pr.Id}'
         style='display:inline-block;margin-top:6px;background:#0d6efd;color:#fff;padding:8px 18px;border-radius:6px;text-decoration:none;font-weight:600'>
        Voir le produit
      </a>
    </div>";
        }));

        var corps = $@"
<!DOCTYPE html><html><body style='font-family:Segoe UI,Arial,sans-serif;background:#f5f6f8;padding:20px'>
  <div style='max-width:560px;margin:auto;background:#fff;border-radius:8px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.06)'>
    <h2 style='color:#0d6efd;margin-top:0'>👋 Vous nous manquez, {WebUtility.HtmlEncode(prenom)} !</h2>
    <p>Cela fait un moment que nous ne vous avons pas vu chez <strong>SODIV Bureau</strong>.</p>
    <p>Pour vous faire plaisir, voici nos promotions du moment dans votre rayon préféré :
       <strong>{WebUtility.HtmlEncode(nomCategorie)}</strong> 🎁</p>
    {blocsProduits}
    <a href='https://sodiv1bureau.runasp.net/Catalogue'
       style='display:inline-block;margin-top:8px;color:#0d6efd;text-decoration:none;font-weight:600'>
      ➜ Découvrir toutes les promotions
    </a>
    <p style='color:#6c757d;font-size:12px;margin-top:24px'>
      Vous recevez cet email car vous possédez un compte client SODIV Bureau.
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
            Subject    = $"🎁 {prenom}, des promotions {nomCategorie} rien que pour vous !",
            Body       = corps,
            IsBodyHtml = true
        };
        mail.To.Add(destinataire);
        smtp.Send(mail);
    }
}
