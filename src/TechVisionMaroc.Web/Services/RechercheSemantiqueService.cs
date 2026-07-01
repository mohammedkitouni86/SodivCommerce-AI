using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TechVisionMaroc.Services;

public interface IRechercheSemantiqueService
{
    /// <summary>
    /// Étend la requête utilisateur en une liste de mots-clés sémantiquement proches
    /// (synonymes français/anglais, variantes, catégories implicites).
    /// Ex : "ordi pas cher" → ["ordinateur","pc","portable","laptop"]
    /// </summary>
    Task<List<string>> EtendreAsync(string requete);
}

public class RechercheSemantiqueService : IRechercheSemantiqueService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RechercheSemantiqueService> _logger;

    public RechercheSemantiqueService(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<RechercheSemantiqueService> logger)
    {
        _http = http;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<string>> EtendreAsync(string requete)
    {
        if (string.IsNullOrWhiteSpace(requete)) return new();

        var cle = "rsem:" + requete.Trim().ToLowerInvariant();
        if (_cache.TryGetValue(cle, out List<string>? cached) && cached != null)
            return cached;

        var apiKey = _config["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("gsk_VOTRE"))
            return new();

        var prompt = $$"""
            Tu aides un moteur de recherche e-commerce marocain (matériel informatique et fournitures de bureau).
            On te donne une requête utilisateur. Réponds UNIQUEMENT par un JSON array de 4 à 8 mots-clés
            (français + anglais courants) qui aideraient à retrouver des produits pertinents.
            - Inclus synonymes, variantes orthographiques, catégorie générique, marques associées si pertinent.
            - Mots courts (1-2 mots max chacun), en minuscules, sans accents.
            - PAS de commentaire, PAS de markdown, UNIQUEMENT le JSON array.

            Requête : "{{requete}}"
            """;

        try
        {
            var body = new
            {
                model = _config["Groq:Model"] ?? "llama-3.3-70b-versatile",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.2,
                max_tokens = 120
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return new();

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var brut = doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";

            var debut = brut.IndexOf('[');
            var fin = brut.LastIndexOf(']');
            if (debut < 0 || fin <= debut) return new();
            var jsonArr = brut.Substring(debut, fin - debut + 1);

            var mots = JsonSerializer.Deserialize<List<string>>(jsonArr) ?? new();
            mots = mots
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Select(m => m.Trim().ToLowerInvariant())
                .Where(m => m.Length > 1 && m.Length < 25)
                .Distinct()
                .Take(8)
                .ToList();

            _cache.Set(cle, mots, TimeSpan.FromHours(2));
            return mots;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec expansion sémantique pour '{Q}'", requete);
            return new();
        }
    }
}
