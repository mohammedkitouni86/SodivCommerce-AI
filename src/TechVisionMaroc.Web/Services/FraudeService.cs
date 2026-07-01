using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public record RegleFraude(string Code, string Description, int Poids);

public interface IFraudeService
{
    Task<EvaluationFraude> EvaluerCommandeAsync(int commandeId, string? ipAdresse, string? userAgent);
    Task<List<EvaluationFraude>> ListerAsync(NiveauRisque? niveau = null, DecisionFraude? decision = null, int max = 100);
    Task DefinirDecisionAsync(int evaluationId, DecisionFraude decision, int? adminId, string? commentaire);
    Task<EvaluationFraude?> ObtenirPourCommandeAsync(int commandeId);
}

public class FraudeService : IFraudeService
{
    private readonly AppDbContext _db;
    private const int SeuilModere   = 30;
    private const int SeuilEleve    = 55;
    private const int SeuilCritique = 80;

    // Domaines d'email jetables connus (extrait, non exhaustif)
    private static readonly HashSet<string> DomainesJetables = new(StringComparer.OrdinalIgnoreCase)
    {
        "mailinator.com", "yopmail.com", "guerrillamail.com", "trashmail.com", "10minutemail.com",
        "tempmail.com", "throwaway.email", "fakeinbox.com", "sharklasers.com", "getairmail.com",
        "maildrop.cc", "tempinbox.com", "mintemail.com", "spam4.me", "tempr.email",
        "dispostable.com", "mvrht.net", "emailondeck.com", "tempemail.com"
    };

    public FraudeService(AppDbContext db) { _db = db; }

    public async Task<EvaluationFraude> EvaluerCommandeAsync(int commandeId, string? ipAdresse, string? userAgent)
    {
        var commande = await _db.Commandes
            .Include(c => c.Utilisateur)
            .Include(c => c.Lignes)
            .FirstOrDefaultAsync(c => c.Id == commandeId)
            ?? throw new InvalidOperationException("Commande introuvable.");

        var utilisateur = commande.Utilisateur;
        var raisons = new List<RegleFraude>();
        var maintenant = DateTime.UtcNow;

        // ── 1. Vitesse : nombre de commandes dans la dernière heure ───────────
        var ilYa1h = maintenant.AddHours(-1);
        var nbCommandes1h = await _db.Commandes
            .CountAsync(c => c.UtilisateurId == commande.UtilisateurId && c.DateCommande >= ilYa1h && c.Id != commande.Id);
        if (nbCommandes1h >= 5)
            raisons.Add(new("VITESSE_TRES_ELEVEE", $"{nbCommandes1h} commandes dans la dernière heure", 35));
        else if (nbCommandes1h >= 2)
            raisons.Add(new("VITESSE_ELEVEE", $"{nbCommandes1h} commandes dans la dernière heure", 15));

        // ── 2. Compte récent (< 24h) avec gros montant ─────────────────────────
        var ageCompte = maintenant - (utilisateur.DateInscription);
        if (ageCompte < TimeSpan.FromHours(1) && commande.Total > 3000)
            raisons.Add(new("COMPTE_NEUF_GROS_MONTANT", $"Compte créé il y a {(int)ageCompte.TotalMinutes} min, montant {commande.Total:N0} MAD", 30));
        else if (ageCompte < TimeSpan.FromDays(1) && commande.Total > 5000)
            raisons.Add(new("COMPTE_RECENT_GROS_MONTANT", $"Compte créé il y a {(int)ageCompte.TotalHours} h, montant {commande.Total:N0} MAD", 20));

        // ── 3. Premier achat avec gros montant ─────────────────────────────────
        var nbCommandesTotal = await _db.Commandes
            .CountAsync(c => c.UtilisateurId == commande.UtilisateurId && c.Id != commande.Id);
        if (nbCommandesTotal == 0 && commande.Total > 8000)
            raisons.Add(new("PREMIER_ACHAT_GROS", $"Premier achat avec montant {commande.Total:N0} MAD", 20));

        // ── 4. Montant inhabituel vs historique (x10) ──────────────────────────
        if (nbCommandesTotal >= 3)
        {
            var moyenne = await _db.Commandes
                .Where(c => c.UtilisateurId == commande.UtilisateurId && c.Id != commande.Id
                            && c.Statut != StatutCommande.Annulee)
                .AverageAsync(c => (decimal?)c.Total) ?? 0m;
            if (moyenne > 0 && commande.Total >= moyenne * 10)
                raisons.Add(new("MONTANT_INHABITUEL", $"Montant {commande.Total:N0} MAD vs moyenne historique {moyenne:N0} MAD (x{(commande.Total/moyenne):N1})", 25));
        }

        // ── 5. Email jetable / suspect ─────────────────────────────────────────
        var email = utilisateur.Email ?? "";
        var domaine = email.Split('@').LastOrDefault() ?? "";
        if (DomainesJetables.Contains(domaine))
            raisons.Add(new("EMAIL_JETABLE", $"Domaine d'email jetable : {domaine}", 35));
        else
        {
            var partieLocale = email.Split('@').FirstOrDefault() ?? "";
            var ratioChiffres = partieLocale.Length > 0
                ? (double)partieLocale.Count(char.IsDigit) / partieLocale.Length : 0;
            if (ratioChiffres > 0.5 && partieLocale.Length > 6)
                raisons.Add(new("EMAIL_SUSPECT", $"Email contient {ratioChiffres:P0} de chiffres", 10));
        }

        // ── 6. Téléphone format inhabituel ─────────────────────────────────────
        var tel = commande.TelephoneLivraison ?? "";
        var telDigits = new string(tel.Where(char.IsDigit).ToArray());
        if (telDigits.Length < 9 || telDigits.Length > 15)
            raisons.Add(new("TEL_FORMAT_SUSPECT", $"Téléphone non standard : {tel}", 10));
        // Tous les chiffres identiques
        if (telDigits.Length >= 9 && telDigits.Distinct().Count() <= 2)
            raisons.Add(new("TEL_REPETITIF", $"Téléphone à chiffres répétitifs : {tel}", 15));

        // ── 7. Adresse trop courte ─────────────────────────────────────────────
        if ((commande.AdresseLivraison ?? "").Trim().Length < 8)
            raisons.Add(new("ADRESSE_INCOMPLETE", "Adresse de livraison trop courte", 12));

        // ── 8. Tentatives de connexion échouées récentes ───────────────────────
        if (utilisateur.TentativesEchouees >= 3)
            raisons.Add(new("CONNEXIONS_ECHOUEES", $"{utilisateur.TentativesEchouees} échecs de connexion récents", 15));

        // ── 9. Quantité anormale d'un même produit (> 10) ──────────────────────
        var maxQte = commande.Lignes.Any() ? commande.Lignes.Max(l => l.Quantite) : 0;
        if (maxQte >= 20)
            raisons.Add(new("QUANTITE_TRES_ELEVEE", $"Ligne avec quantité {maxQte}", 20));
        else if (maxQte >= 10)
            raisons.Add(new("QUANTITE_ELEVEE", $"Ligne avec quantité {maxQte}", 10));

        // ── 10. Commande > 25 000 MAD (très élevée pour Maroc B2C) ─────────────
        if (commande.Total >= 50000)
            raisons.Add(new("MONTANT_TRES_ELEVE", $"Total {commande.Total:N0} MAD", 25));
        else if (commande.Total >= 25000)
            raisons.Add(new("MONTANT_ELEVE", $"Total {commande.Total:N0} MAD", 12));

        // ── 11. User-agent vide ou suspect (bot) ───────────────────────────────
        if (string.IsNullOrWhiteSpace(userAgent))
            raisons.Add(new("USER_AGENT_VIDE", "User-Agent absent (potentiel bot)", 15));
        else if (Regex.IsMatch(userAgent, "(bot|crawl|spider|curl|python|wget|java/)", RegexOptions.IgnoreCase))
            raisons.Add(new("USER_AGENT_BOT", $"User-Agent suspect : {userAgent[..Math.Min(60, userAgent.Length)]}", 25));

        // ── 12. Heure inhabituelle (2h-5h heure du Maroc) ──────────────────────
        var heureLocale = TimeZoneInfo.ConvertTimeFromUtc(maintenant,
            TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "Morocco Standard Time" : "Africa/Casablanca")).Hour;
        if (heureLocale >= 2 && heureLocale <= 5 && commande.Total > 5000)
            raisons.Add(new("HEURE_INHABITUELLE", $"Commande à {heureLocale}h heure marocaine (montant élevé)", 8));

        // ── 13. Email contient un nombre absurde de caractères spéciaux ────────
        if (email.Count(c => !char.IsLetterOrDigit(c) && c != '@' && c != '.') > 3)
            raisons.Add(new("EMAIL_CARACTERES_SPECIAUX", "Email contient beaucoup de caractères spéciaux", 8));

        // ── 14. Bonus : pénalité paiement en espèces sur grosse commande ───────
        if (commande.MethodePaiement == MethodePaiement.Especes && commande.Total > 15000)
            raisons.Add(new("ESPECES_GROS_MONTANT", $"Paiement à la livraison pour {commande.Total:N0} MAD", 12));

        // ── Calcul score & niveau ─────────────────────────────────────────────
        var score = Math.Min(100, raisons.Sum(r => r.Poids));
        var niveau = score switch
        {
            >= SeuilCritique => NiveauRisque.Critique,
            >= SeuilEleve    => NiveauRisque.Eleve,
            >= SeuilModere   => NiveauRisque.Modere,
            _                => NiveauRisque.Faible
        };
        var decision = niveau switch
        {
            NiveauRisque.Critique => DecisionFraude.Bloquee,
            NiveauRisque.Eleve    => DecisionFraude.AReviser,
            _                     => DecisionFraude.Validee
        };

        // Pas de doublon
        var existant = await _db.EvaluationsFraude.FirstOrDefaultAsync(e => e.CommandeId == commandeId);
        if (existant != null)
        {
            existant.Score = score;
            existant.Niveau = niveau;
            existant.Decision = decision;
            existant.Raisons = JsonSerializer.Serialize(raisons);
            existant.IpAdresse = ipAdresse;
            existant.UserAgent = userAgent;
            existant.DateEvaluation = maintenant;
            await _db.SaveChangesAsync();
            return existant;
        }

        var eval = new EvaluationFraude
        {
            CommandeId = commandeId,
            UtilisateurId = commande.UtilisateurId,
            Score = score,
            Niveau = niveau,
            Decision = decision,
            Raisons = JsonSerializer.Serialize(raisons),
            IpAdresse = ipAdresse,
            UserAgent = userAgent,
            DateEvaluation = maintenant
        };
        _db.EvaluationsFraude.Add(eval);

        // Si critique → annuler automatiquement la commande
        if (decision == DecisionFraude.Bloquee)
            commande.Statut = StatutCommande.Annulee;

        await _db.SaveChangesAsync();
        return eval;
    }

    public async Task<List<EvaluationFraude>> ListerAsync(NiveauRisque? niveau = null, DecisionFraude? decision = null, int max = 100)
    {
        var q = _db.EvaluationsFraude
            .Include(e => e.Commande).ThenInclude(c => c.Utilisateur)
            .AsQueryable();
        if (niveau.HasValue)   q = q.Where(e => e.Niveau == niveau.Value);
        if (decision.HasValue) q = q.Where(e => e.Decision == decision.Value);
        return await q.OrderByDescending(e => e.Score).ThenByDescending(e => e.DateEvaluation).Take(max).ToListAsync();
    }

    public async Task DefinirDecisionAsync(int evaluationId, DecisionFraude decision, int? adminId, string? commentaire)
    {
        var eval = await _db.EvaluationsFraude
            .Include(e => e.Commande)
            .FirstOrDefaultAsync(e => e.Id == evaluationId);
        if (eval == null) return;
        eval.Decision = decision;
        eval.DateRevision = DateTime.UtcNow;
        eval.ReviseParId = adminId;
        eval.CommentaireRevision = commentaire;

        // Synchronise le statut de la commande
        if (eval.Commande != null)
        {
            if (decision == DecisionFraude.Bloquee && eval.Commande.Statut != StatutCommande.Remboursee)
                eval.Commande.Statut = StatutCommande.Annulee;
            else if (decision == DecisionFraude.Validee && eval.Commande.Statut == StatutCommande.Annulee)
                eval.Commande.Statut = StatutCommande.Confirmee;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<EvaluationFraude?> ObtenirPourCommandeAsync(int commandeId) =>
        await _db.EvaluationsFraude.FirstOrDefaultAsync(e => e.CommandeId == commandeId);
}
