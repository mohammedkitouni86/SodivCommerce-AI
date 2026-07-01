namespace TechVisionMaroc.Services;

/// <summary>
/// Recalcule les scores RFM toutes les 6 heures.
/// </summary>
public class SegmentationJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SegmentationJob> _log;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public SegmentationJob(IServiceScopeFactory scopes, ILogger<SegmentationJob> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Attendre 30s après démarrage pour ne pas surcharger
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ISegmentationService>();
                var n = await svc.RecalculerTousAsync(ct);
                _log.LogInformation("Segmentation RFM recalculée : {N} clients", n);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Échec recalcul segmentation RFM");
            }

            try { await Task.Delay(Interval, ct); } catch { break; }
        }
    }
}
