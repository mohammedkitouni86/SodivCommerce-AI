using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;
using TechVisionMaroc.Services;

namespace TechVisionMaroc.Controllers;

[ApiController]
[Route("track")]
[IgnoreAntiforgeryToken]
public class TrackController : ControllerBase
{
    private readonly ITrackingService _track;
    private readonly AppDbContext _db;
    public TrackController(ITrackingService track, AppDbContext db) { _track = track; _db = db; }

    /// <summary>
    /// Renvoie le nombre de visiteurs actifs (sessions uniques avec un événement dans les 5 dernières minutes).
    /// Endpoint public (public dashboard + admin).
    /// </summary>
    [HttpGet("visiteurs-en-ligne")]
    public async Task<IActionResult> VisiteursEnLigne()
    {
        var depuis = DateTime.UtcNow.AddMinutes(-5);
        var count = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis)
            .Select(e => e.SessionId)
            .Distinct()
            .CountAsync();
        // Inclut au moins 1 (le visiteur courant)
        if (count < 1) count = 1;
        return Ok(new { count, periode = "5 minutes" });
    }

    /// <summary>
    /// Détail des visiteurs actifs : compte total + répartition par appareil + dernières pages vues.
    /// Pour le dashboard admin.
    /// </summary>
    [HttpGet("visiteurs-en-ligne/detail")]
    public async Task<IActionResult> VisiteursEnLigneDetail()
    {
        var depuis = DateTime.UtcNow.AddMinutes(-5);
        var evts = await _db.EvenementsComportement
            .Where(e => e.Date >= depuis)
            .OrderByDescending(e => e.Date)
            .Select(e => new {
                e.SessionId,
                e.Appareil,
                e.Page,
                e.Date,
                e.UtilisateurId
            })
            .ToListAsync();

        // Groupe par session (1 session = 1 visiteur)
        var sessions = evts.GroupBy(e => e.SessionId)
            .Select(g => new {
                SessionId = g.Key,
                DerniereActivite = g.Max(e => e.Date),
                DerniereePage    = g.OrderByDescending(e => e.Date).First().Page,
                Appareil         = g.First().Appareil,
                NbActions        = g.Count(),
                Connecte         = g.Any(e => e.UtilisateurId.HasValue)
            })
            .OrderByDescending(s => s.DerniereActivite)
            .ToList();

        var repartition = sessions.GroupBy(s => s.Appareil)
            .Select(g => new { Appareil = g.Key.ToString(), Nb = g.Count() })
            .ToList();

        return Ok(new {
            total = sessions.Count,
            connectes = sessions.Count(s => s.Connecte),
            anonymes  = sessions.Count(s => !s.Connecte),
            repartition,
            sessions = sessions.Take(20)
        });
    }

    public record TrackPayload(
        string Type,
        string? CibleType,
        int? CibleId,
        string? Etiquette,
        decimal? Valeur,
        int? DureeMs,
        string? Metadonnees);

    [HttpPost("")]
    public async Task<IActionResult> Index([FromBody] TrackPayload payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.Type))
            return BadRequest();
        if (!Enum.TryParse<TypeEvenement>(payload.Type, ignoreCase: true, out var t))
            return BadRequest();

        await _track.EnregistrerAsync(t, HttpContext,
            cibleType: payload.CibleType,
            cibleId: payload.CibleId,
            etiquette: payload.Etiquette,
            valeur: payload.Valeur,
            dureeMs: payload.DureeMs,
            metadonnees: payload.Metadonnees);
        return Ok(new { ok = true });
    }
}
