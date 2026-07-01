using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPanierService _panier;
    private readonly IFideliteService _fidelite;
    private readonly IConfiguration _config;

    private const int MaxTentatives   = 5;
    private const int DureeBloquageMin = 15;

    public AccountController(AppDbContext db, IPanierService panier, IFideliteService fidelite, IConfiguration config)
    {
        _db = db;
        _panier = panier;
        _fidelite = fidelite;
        _config = config;
    }

    // Destination après connexion : tableau de bord pour les admins, accueil pour les clients.
    private string DestinationApresConnexion(string? role)
    {
        if (role == "Admin" || role == "SuperAdmin")
            return "/" + (_config["Admin:RoutePrefix"] ?? "gestion-tv-9f3k").TrimStart('/');
        return "/";
    }

    // ── Connexion ────────────────────────────────────────────────────────────

    public IActionResult Connexion(string? returnUrl) { ViewBag.ReturnUrl = returnUrl; return View(); }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Connexion(string email, string motDePasse, string? returnUrl)
    {
        ViewBag.ReturnUrl = returnUrl;

        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null)
        {
            ModelState.AddModelError("", "Email ou mot de passe incorrect.");
            return View();
        }

        // Compte désactivé
        if (!user.EstActif)
        {
            ModelState.AddModelError("", "Ce compte a été désactivé. Contactez le support.");
            return View();
        }

        // Compte bloqué (trop de tentatives)
        if (user.BloqueJusqua.HasValue && user.BloqueJusqua > DateTime.UtcNow)
        {
            var restant = (int)Math.Ceiling((user.BloqueJusqua.Value - DateTime.UtcNow).TotalMinutes);
            ModelState.AddModelError("", $"Compte temporairement bloqué. Réessayez dans {restant} minute(s).");
            return View();
        }

        // Vérification mot de passe
        if (!BCrypt.Net.BCrypt.Verify(motDePasse, user.MotDePasseHash))
        {
            user.TentativesEchouees++;
            if (user.TentativesEchouees >= MaxTentatives)
            {
                user.BloqueJusqua = DateTime.UtcNow.AddMinutes(DureeBloquageMin);
                user.TentativesEchouees = 0;
                await _db.SaveChangesAsync();
                ModelState.AddModelError("", $"Trop de tentatives échouées. Compte bloqué {DureeBloquageMin} minutes.");
                return View();
            }
            var restantes = MaxTentatives - user.TentativesEchouees;
            await _db.SaveChangesAsync();
            ModelState.AddModelError("", $"Mot de passe incorrect. {restantes} tentative(s) restante(s) avant blocage.");
            return View();
        }

        // Succès → réinitialiser compteur
        user.TentativesEchouees = 0;
        user.BloqueJusqua = null;
        user.DerniereConnexion = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // 2FA réservée aux comptes Admin/SuperAdmin uniquement (clients exemptés)
        bool estAdmin = user.Role == "Admin" || user.Role == "SuperAdmin";
        if (estAdmin && user.TotpActive && !string.IsNullOrEmpty(user.TotpSecret))
        {
            HttpContext.Session.SetInt32("TotpPendingUserId", user.Id);
            HttpContext.Session.SetString("TotpReturnUrl", returnUrl ?? "/");
            return RedirectToAction(nameof(Totp));
        }

        var sessionId = HttpContext.Session.Id;
        HttpContext.Session.SetInt32("UtilisateurId", user.Id);
        HttpContext.Session.SetString("UtilisateurNom", $"{user.Prenom} {user.Nom}");
        HttpContext.Session.SetString("UtilisateurEmail", user.Email);
        HttpContext.Session.SetString("UtilisateurRole", user.Role);
        HttpContext.Session.SetString("DerniereActivite", DateTime.UtcNow.ToString("o"));

        await _panier.FusionnerPaniersAsync(sessionId, user.Id);

        // Admin/SuperAdmin → tableau de bord ; client → accueil du site.
        return LocalRedirect(DestinationApresConnexion(user.Role));
    }

    [HttpGet]
    public IActionResult Totp()
    {
        if (HttpContext.Session.GetInt32("TotpPendingUserId") == null)
            return RedirectToAction(nameof(Connexion));
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Totp(string code)
    {
        var pendingId = HttpContext.Session.GetInt32("TotpPendingUserId");
        if (pendingId == null) return RedirectToAction(nameof(Connexion));

        var user = await _db.Utilisateurs.FindAsync(pendingId.Value);
        if (user == null || string.IsNullOrEmpty(user.TotpSecret))
        {
            HttpContext.Session.Remove("TotpPendingUserId");
            return RedirectToAction(nameof(Connexion));
        }

        if (!TotpService.VerifierCode(user.TotpSecret, code ?? ""))
        {
            ViewBag.Error = "Code incorrect.";
            return View();
        }

        HttpContext.Session.Remove("TotpPendingUserId");
        HttpContext.Session.Remove("TotpReturnUrl");

        var sessionId = HttpContext.Session.Id;
        HttpContext.Session.SetInt32("UtilisateurId", user.Id);
        HttpContext.Session.SetString("UtilisateurNom", $"{user.Prenom} {user.Nom}");
        HttpContext.Session.SetString("UtilisateurEmail", user.Email);
        HttpContext.Session.SetString("UtilisateurRole", user.Role);
        HttpContext.Session.SetString("DerniereActivite", DateTime.UtcNow.ToString("o"));
        await _panier.FusionnerPaniersAsync(sessionId, user.Id);
        // 2FA réservée aux admins → tableau de bord.
        return LocalRedirect(DestinationApresConnexion(user.Role));
    }

    // ── Inscription ───────────────────────────────────────────────────────────

    public IActionResult Inscription(string? parrain)
    {
        // Mémorise le code de parrainage reçu via le lien (?parrain=SODIVxxxxx)
        if (!string.IsNullOrWhiteSpace(parrain))
            HttpContext.Session.SetString("ParrainCode", parrain.Trim());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Inscription(
        string prenom, string nom, string email, string telephone,
        string motDePasse, string confirmer, string? dateNaissance)
    {
        // Garde : champs obligatoires (évite un NullReferenceException → 500 sur formulaire vide)
        if (string.IsNullOrWhiteSpace(prenom) || string.IsNullOrWhiteSpace(nom) ||
            string.IsNullOrWhiteSpace(email)  || string.IsNullOrWhiteSpace(motDePasse) ||
            string.IsNullOrWhiteSpace(confirmer))
        {
            ModelState.AddModelError("", "Veuillez remplir tous les champs obligatoires.");
            return View();
        }

        // Correspondance mots de passe
        if (motDePasse != confirmer)
        {
            ModelState.AddModelError("", "Les mots de passe ne correspondent pas.");
            return View();
        }

        // Force du mot de passe
        var erreurMdp = ValiderMotDePasse(motDePasse);
        if (erreurMdp != null)
        {
            ModelState.AddModelError("", erreurMdp);
            return View();
        }

        // Email unique
        if (await _db.Utilisateurs.AnyAsync(u => u.Email == email))
        {
            ModelState.AddModelError("", "Un compte existe déjà avec cet email.");
            return View();
        }

        // Vérification âge (>= 18 ans)
        DateTime? naissance = null;
        if (!string.IsNullOrEmpty(dateNaissance) && DateTime.TryParse(dateNaissance, out var dn))
        {
            naissance = dn;
            var age = DateTime.Today.Year - dn.Year;
            if (dn.Date > DateTime.Today.AddYears(-age)) age--;
            if (age < 18)
            {
                ModelState.AddModelError("", "Vous devez avoir au moins 18 ans pour créer un compte.");
                return View();
            }
        }
        else
        {
            ModelState.AddModelError("", "La date de naissance est obligatoire.");
            return View();
        }

        var user = new Utilisateur
        {
            Prenom          = prenom.Trim(),
            Nom             = nom.Trim(),
            Email           = email.Trim().ToLower(),
            Telephone       = telephone?.Trim() ?? "",
            DateNaissance   = naissance,
            MotDePasseHash  = BCrypt.Net.BCrypt.HashPassword(motDePasse)
        };

        _db.Utilisateurs.Add(user);
        await _db.SaveChangesAsync();

        // ── Parrainage : lier le filleul à son parrain (code SODIVxxxxx) ──
        var codeParrain = HttpContext.Session.GetString("ParrainCode");
        if (!string.IsNullOrWhiteSpace(codeParrain) &&
            codeParrain.StartsWith("SODIV", StringComparison.OrdinalIgnoreCase))
        {
            var numero = new string(codeParrain.Where(char.IsDigit).ToArray());
            if (int.TryParse(numero, out var parrainId) && parrainId != user.Id &&
                await _db.Utilisateurs.AnyAsync(u => u.Id == parrainId))
            {
                user.ParrainId = parrainId;
                await _db.SaveChangesAsync();
            }
            HttpContext.Session.Remove("ParrainCode");
        }

        HttpContext.Session.SetInt32("UtilisateurId", user.Id);
        HttpContext.Session.SetString("UtilisateurNom", $"{user.Prenom} {user.Nom}");
        HttpContext.Session.SetString("UtilisateurRole", user.Role);
        HttpContext.Session.SetString("DerniereActivite", DateTime.UtcNow.ToString("o"));

        TempData["Success"] = "Bienvenue sur SODIV Bureau !";
        return RedirectToAction("Index", "Home");
    }

    // ── Déconnexion ───────────────────────────────────────────────────────────

    public IActionResult Deconnexion()
    {
        HttpContext.Session.Clear();
        return RedirectToAction("Index", "Home");
    }

    // ── Profil ────────────────────────────────────────────────────────────────

    public async Task<IActionResult> Profil()
    {
        var id = HttpContext.Session.GetInt32("UtilisateurId");
        if (id == null) return RedirectToAction(nameof(Connexion));
        var user = await _db.Utilisateurs.Include(u => u.Commandes).FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) { HttpContext.Session.Clear(); return RedirectToAction(nameof(Connexion)); }
        return View(user);
    }

    // ── Programme de fidélité ──────────────────────────────────────────────
    [HttpGet("/compte/fidelite")]
    public async Task<IActionResult> Fidelite()
    {
        var id = HttpContext.Session.GetInt32("UtilisateurId");
        if (id == null) return RedirectToAction(nameof(Connexion), new { returnUrl = "/compte/fidelite" });

        ViewBag.Solde      = await _fidelite.SoldeAsync(id.Value);
        ViewBag.Historique = await _fidelite.HistoriqueAsync(id.Value, 200);
        ViewBag.Niveau     = await _fidelite.NiveauAsync(id.Value);
        ViewBag.Expirant   = await _fidelite.PointsExpirantBientotAsync(id.Value, TimeSpan.FromDays(60));
        ViewBag.ValeurPoint= _fidelite.ValeurPoint;
        ViewBag.PctMax     = _fidelite.PourcentageMaxReduction;

        var user = await _db.Utilisateurs.FindAsync(id.Value);
        return View(user);
    }

    [HttpGet]
    public async Task<IActionResult> ModifierProfil()
    {
        var id = HttpContext.Session.GetInt32("UtilisateurId");
        if (id == null) return RedirectToAction(nameof(Connexion));
        var user = await _db.Utilisateurs.FindAsync(id);
        if (user == null) { HttpContext.Session.Clear(); return RedirectToAction(nameof(Connexion)); }
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ModifierProfil(
        string prenom, string nom, string email, string? telephone, string? adresse, string? ville)
    {
        var id = HttpContext.Session.GetInt32("UtilisateurId");
        if (id == null) return RedirectToAction(nameof(Connexion));

        var user = await _db.Utilisateurs.FindAsync(id);
        if (user == null) { HttpContext.Session.Clear(); return RedirectToAction(nameof(Connexion)); }

        if (string.IsNullOrWhiteSpace(prenom) || string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Prénom, nom et email sont obligatoires.";
            return View(user);
        }

        var emailNorm = email.Trim().ToLower();
        if (emailNorm != user.Email &&
            await _db.Utilisateurs.AnyAsync(u => u.Email == emailNorm && u.Id != user.Id))
        {
            TempData["Error"] = "Cet email est déjà utilisé par un autre compte.";
            return View(user);
        }

        user.Prenom    = prenom.Trim();
        user.Nom       = nom.Trim();
        user.Email     = emailNorm;
        user.Telephone = telephone?.Trim() ?? "";
        user.Adresse   = adresse?.Trim() ?? "";
        user.Ville     = ville?.Trim() ?? "";

        await _db.SaveChangesAsync();
        HttpContext.Session.SetString("UtilisateurNom", $"{user.Prenom} {user.Nom}");
        TempData["Success"] = "Profil mis à jour avec succès.";
        return RedirectToAction(nameof(Profil));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangerMotDePasse(string motDePasseActuel, string nouveauMotDePasse, string confirmation)
    {
        var id = HttpContext.Session.GetInt32("UtilisateurId");
        if (id == null) return RedirectToAction(nameof(Connexion));

        var user = await _db.Utilisateurs.FindAsync(id);
        if (user == null) { HttpContext.Session.Clear(); return RedirectToAction(nameof(Connexion)); }

        if (!BCrypt.Net.BCrypt.Verify(motDePasseActuel ?? "", user.MotDePasseHash))
        {
            TempData["Error"] = "Mot de passe actuel incorrect.";
            return RedirectToAction(nameof(ModifierProfil));
        }
        if (nouveauMotDePasse != confirmation)
        {
            TempData["Error"] = "La confirmation ne correspond pas au nouveau mot de passe.";
            return RedirectToAction(nameof(ModifierProfil));
        }
        var erreur = ValiderMotDePasse(nouveauMotDePasse ?? "");
        if (erreur != null)
        {
            TempData["Error"] = erreur;
            return RedirectToAction(nameof(ModifierProfil));
        }

        user.MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(nouveauMotDePasse);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Mot de passe changé avec succès.";
        return RedirectToAction(nameof(Profil));
    }

    // ── Ping (renouvellement session inactivité) ──────────────────────────────

    [HttpPost]
    public IActionResult Ping()
    {
        if (HttpContext.Session.GetInt32("UtilisateurId") != null)
            HttpContext.Session.SetString("DerniereActivite", DateTime.UtcNow.ToString("o"));
        return Ok();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ValiderMotDePasse(string mdp)
    {
        if (mdp.Length < 8)
            return "Le mot de passe doit contenir au moins 8 caractères.";
        if (!Regex.IsMatch(mdp, @"[A-Z]"))
            return "Le mot de passe doit contenir au moins une lettre majuscule.";
        if (!Regex.IsMatch(mdp, @"[a-z]"))
            return "Le mot de passe doit contenir au moins une lettre minuscule.";
        if (!Regex.IsMatch(mdp, @"[0-9]"))
            return "Le mot de passe doit contenir au moins un chiffre.";
        return null;
    }
}
