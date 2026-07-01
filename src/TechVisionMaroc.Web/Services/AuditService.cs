using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IAuditService
{
    Task LogAsync(string action, string? cible = null, string? details = null);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _ctx;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext db, IHttpContextAccessor ctx, ILogger<AuditService> logger)
    {
        _db = db;
        _ctx = ctx;
        _logger = logger;
    }

    public async Task LogAsync(string action, string? cible = null, string? details = null)
    {
        try
        {
            var c = _ctx.HttpContext;
            var log = new AuditLog
            {
                UtilisateurId    = c?.Session.GetInt32("UtilisateurId"),
                UtilisateurEmail = c?.Session.GetString("UtilisateurEmail"),
                Role             = c?.Session.GetString("UtilisateurRole"),
                Action           = action,
                Cible            = cible,
                Details          = details,
                IpAdresse        = c?.Connection.RemoteIpAddress?.ToString(),
                UserAgent        = c?.Request.Headers.UserAgent.ToString(),
                Date             = DateTime.UtcNow
            };
            _db.AuditLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec écriture audit log");
        }
    }
}
