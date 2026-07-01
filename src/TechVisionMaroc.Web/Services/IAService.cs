namespace TechVisionMaroc.Services;

public interface IIAService
{
    Task<List<int>> ObtenirRecommandationsAsync(int produitId, int nombre);
    Task<string> AnalyserSentimentAsync(string texte);
    Task<List<int>> ObtenirTendancesAsync(int nombre);
}

public class IAService : IIAService
{
    private readonly HttpClient _http;
    private readonly ILogger<IAService> _logger;

    public IAService(HttpClient http, ILogger<IAService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<List<int>> ObtenirRecommandationsAsync(int produitId, int nombre)
    {
        var response = await _http.GetFromJsonAsync<RecommandationResponse>(
            $"/api/recommandations/{produitId}?nombre={nombre}");
        return response?.Ids ?? new List<int>();
    }

    public async Task<string> AnalyserSentimentAsync(string texte)
    {
        var response = await _http.PostAsJsonAsync("/api/sentiment", new { texte });
        if (!response.IsSuccessStatusCode) return "Neutre";
        var result = await response.Content.ReadFromJsonAsync<SentimentResponse>();
        return result?.Sentiment ?? "Neutre";
    }

    public async Task<List<int>> ObtenirTendancesAsync(int nombre)
    {
        var response = await _http.GetFromJsonAsync<RecommandationResponse>(
            $"/api/tendances?nombre={nombre}");
        return response?.Ids ?? new List<int>();
    }

    private record RecommandationResponse(List<int> Ids);
    private record SentimentResponse(string Sentiment, double Score);
}
