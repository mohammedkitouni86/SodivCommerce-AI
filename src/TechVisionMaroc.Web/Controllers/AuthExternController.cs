using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("AuthExtern")]
public class AuthExternController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPanierService _panier;
    private readonly IConfiguration _cfg;

    public AuthExternController(AppDbContext db, IPanierService panier, IConfiguration cfg)
    {
        _db = db; _panier = panier; _cfg = cfg;
    }

    /// <summary>Démarre le flow OAuth Google (challenge).</summary>
    [HttpGet("google")]
    public IActionResult Google(string? returnUrl = "/")
    {
        var googleId = _cfg["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(googleId) || googleId.StartsWith("VOTRE_"))
        {
            TempData["Error"] = "Login Google non configuré. Ajoutez votre ClientId/Secret dans appsettings.json.";
            return RedirectToAction("Connexion", "Account");
        }

        var props = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleCallback), new { returnUrl })!
        };
        return Challenge(props, GoogleDefaults.AuthenticationScheme);
    }

    /// <summary>Callback OAuth Google après autorisation utilisateur.</summary>
    [HttpGet("google-callback")]
    public async Task<IActionResult> GoogleCallback(string? returnUrl = "/")
    {
        var result = await HttpContext.AuthenticateAsync();
        if (result?.Principal == null)
        {
            TempData["Error"] = "Échec de l'authentification Google.";
            return RedirectToAction("Connexion", "Account");
        }

        var email = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var nom    = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.Surname)?.Value ?? "";
        var prenom = result.Principal.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? "";

        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "Email Google introuvable.";
            return RedirectToAction("Connexion", "Account");
        }

        var user = await _db.Utilisateurs.FirstOrDefaultAsync(u => u.Email == email.ToLower());
        if (user == null)
        {
            // Création automatique du compte
            user = new Utilisateur
            {
                Email = email.ToLower(),
                Nom = string.IsNullOrWhiteSpace(nom) ? "Utilisateur" : nom,
                Prenom = prenom,
                MotDePasseHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                Role = "Client",
                EstActif = true,
                DateInscription = DateTime.UtcNow,
                Telephone = "",
                Adresse = "",
                Ville = ""
            };
            _db.Utilisateurs.Add(user);
            await _db.SaveChangesAsync();
        }

        // Connexion
        user.DerniereConnexion = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var sessionId = HttpContext.Session.Id;
        HttpContext.Session.SetInt32("UtilisateurId", user.Id);
        HttpContext.Session.SetString("UtilisateurNom", $"{user.Prenom} {user.Nom}".Trim());
        HttpContext.Session.SetString("UtilisateurEmail", user.Email);
        HttpContext.Session.SetString("UtilisateurRole", user.Role);
        HttpContext.Session.SetString("DerniereActivite", DateTime.UtcNow.ToString("o"));
        await _panier.FusionnerPaniersAsync(sessionId, user.Id);

        TempData["Success"] = $"Bienvenue {user.Prenom} ! Connecté avec Google.";
        return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }
}
