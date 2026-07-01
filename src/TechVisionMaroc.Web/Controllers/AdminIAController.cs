using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("api/admin/ia")]
public class AdminIAController : Controller
{
    private readonly IGenerationDescriptionService _ia;
    private readonly AppDbContext _db;

    public AdminIAController(IGenerationDescriptionService ia, AppDbContext db)
    {
        _ia = ia;
        _db = db;
    }

    public record GenererDescEntree(string Nom, string Marque, int? CategorieId, decimal? Prix);

    [HttpPost("description")]
    public async Task<IActionResult> GenererDescription([FromBody] GenererDescEntree e)
    {
        var role = HttpContext.Session.GetString("UtilisateurRole") ?? "";
        if (role != "Admin" && role != "SuperAdmin")
            return Unauthorized(new { erreur = "Accès réservé aux administrateurs." });

        if (e == null || string.IsNullOrWhiteSpace(e.Nom) || string.IsNullOrWhiteSpace(e.Marque))
            return BadRequest(new { erreur = "Nom et marque requis." });

        string? nomCat = null;
        if (e.CategorieId is int cid)
            nomCat = await _db.Categories.Where(c => c.Id == cid).Select(c => c.Nom).FirstOrDefaultAsync();

        var desc = await _ia.GenererAsync(e.Nom.Trim(), e.Marque.Trim(), nomCat, e.Prix);
        if (string.IsNullOrWhiteSpace(desc))
            return StatusCode(503, new { erreur = "Génération IA indisponible. Vérifiez la clé Groq ou réessayez." });

        return Json(new { description = desc });
    }
}
