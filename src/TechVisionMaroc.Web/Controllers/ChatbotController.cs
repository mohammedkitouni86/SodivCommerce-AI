using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TechVisionMaroc.Data;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[Route("api/chatbot")]
public class ChatbotController : Controller
{
    private readonly IChatbotService _chatbot;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private const int MaxHistorique = 12;
    private const string SessionKey = "ChatbotHistorique";

    public ChatbotController(IChatbotService chatbot, AppDbContext db, IWebHostEnvironment env)
    {
        _chatbot = chatbot;
        _db = db;
        _env = env;
    }

    public record MessageEntrant(string Message);

    [HttpPost("message")]
    public async Task<IActionResult> Message([FromBody] MessageEntrant entree)
    {
        if (entree == null || string.IsNullOrWhiteSpace(entree.Message))
            return BadRequest(new { erreur = "Message vide." });
        if (entree.Message.Length > 500)
            return BadRequest(new { erreur = "Message trop long (500 caractères max)." });

        // Charger historique session
        var historique = ChargerHistorique();

        var rep = await _chatbot.RepondreAsync(entree.Message, historique);

        // Mettre à jour l'historique
        historique.Add(new ChatbotMessage("user",      entree.Message));
        historique.Add(new ChatbotMessage("assistant", rep.Reponse));
        if (historique.Count > MaxHistorique)
            historique = historique.TakeLast(MaxHistorique).ToList();
        HttpContext.Session.SetString(SessionKey, JsonSerializer.Serialize(historique));

        return Json(new { reponse = rep.Reponse, produits = rep.Produits });
    }

    /// <summary>Recherche de produits à partir d'une photo envoyée dans le chat.</summary>
    [HttpPost("image")]
    public async Task<IActionResult> Image(IFormFile? image)
    {
        if (image == null || image.Length == 0)
            return BadRequest(new { erreur = "Aucune image reçue." });
        if (image.Length > 8 * 1024 * 1024)
            return BadRequest(new { erreur = "Image trop volumineuse (max 8 Mo)." });

        using var ms = new MemoryStream();
        await image.CopyToAsync(ms);

        var resultats = await ImageSimilariteService.RechercherAsync(_db, _env, ms.ToArray(), 6);
        if (resultats.Count == 0)
            return Json(new
            {
                reponse = "Je n'ai pas trouvé de produit correspondant à cette photo dans notre catalogue. 😕 Essayez une autre image, ou décrivez-moi ce que vous cherchez.",
                produits = Array.Empty<object>()
            });

        var ids = resultats.Select(r => r.Id).ToList();
        // Photo qui a réellement correspondu (peut être une des 4 photos, pas la principale).
        var urlParId = resultats.ToDictionary(r => r.Id, r => r.MatchedUrl);
        var trouves = await _db.Produits
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Nom, Prix = p.PrixPromo ?? p.Prix, p.ImageUrl })
            .ToListAsync();
        // Conserver l'ordre par pertinence + afficher la PHOTO correspondante.
        var produits = ids.Select(id => {
            var t = trouves.First(x => x.Id == id);
            var img = (urlParId.TryGetValue(id, out var u) && !string.IsNullOrEmpty(u)) ? u : t.ImageUrl;
            return new ProduitSuggere(t.Id, t.Nom, t.Prix, img, "/Produit/Details/" + t.Id);
        }).ToList();

        return Json(new
        {
            reponse = $"📸 J'ai trouvé {produits.Count} produit(s) qui ressemble(nt) à votre photo :",
            produits
        });
    }

    [HttpPost("reset")]
    public IActionResult Reset()
    {
        HttpContext.Session.Remove(SessionKey);
        return Ok();
    }

    private List<ChatbotMessage> ChargerHistorique()
    {
        var json = HttpContext.Session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json)) return new();
        try { return JsonSerializer.Deserialize<List<ChatbotMessage>>(json) ?? new(); }
        catch { return new(); }
    }
}
