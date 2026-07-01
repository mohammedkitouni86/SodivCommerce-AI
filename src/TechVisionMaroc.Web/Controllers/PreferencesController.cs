using Microsoft.AspNetCore.Mvc;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("preferences")]
public class PreferencesController : Controller
{
    /// <summary>Bascule HT/TTC. Cookie persisté 1 an.</summary>
    [HttpGet("prix")]
    public IActionResult Prix(string mode, string? retour)
    {
        var valeur = string.Equals(mode, "HT", StringComparison.OrdinalIgnoreCase) ? "HT" : "TTC";

        Response.Cookies.Append(PrixService.CookieName, valeur, new CookieOptions
        {
            Expires   = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly  = false,
            IsEssential = true,
            SameSite  = SameSiteMode.Lax
        });

        var url = string.IsNullOrWhiteSpace(retour) ? Request.Headers.Referer.ToString() : retour;
        if (string.IsNullOrWhiteSpace(url) || !Url.IsLocalUrl(url)) url = "/";
        return Redirect(url);
    }
}
