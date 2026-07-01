using System.Text;
using System.Text.Json;

namespace TechVisionMaroc.Services;

public interface IGenerationDescriptionService
{
    Task<string?> GenererAsync(string nom, string marque, string? categorie, decimal? prix);
}

public class GenerationDescriptionService : IGenerationDescriptionService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<GenerationDescriptionService> _logger;

    public GenerationDescriptionService(HttpClient http, IConfiguration config, ILogger<GenerationDescriptionService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<string?> GenererAsync(string nom, string marque, string? categorie, decimal? prix)
    {
        var apiKey = _config["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("gsk_VOTRE"))
        {
            _logger.LogWarning("Clé Groq absente — génération impossible.");
            return null;
        }

        var prixStr = prix.HasValue ? $"{prix.Value:N0} MAD" : "non précisé";
        var prompt = $$"""
            Tu es rédacteur e-commerce pour SODIV Bureau (Salé, Maroc).
            Génère une description commerciale pour ce produit :
            - Nom : {{nom}}
            - Marque : {{marque}}
            - Catégorie : {{categorie ?? "non précisée"}}
            - Prix : {{prixStr}}

            Règles :
            - 3 à 4 phrases (max 400 caractères)
            - Ton commercial et professionnel, en français
            - Mentionne 2-3 caractéristiques probables selon la catégorie/marque
            - Termine par un mini argument de vente
            - PAS de markdown, PAS de listes, PAS d'emoji
            - Réponds UNIQUEMENT avec la description, sans préfixe ni guillemets
            """;

        try
        {
            var body = new
            {
                model = _config["Groq:Model"] ?? "llama-3.3-70b-versatile",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.7,
                max_tokens = 250
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq error {Code}: {Body}", resp.StatusCode, json);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString()?.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec génération description IA");
            return null;
        }
    }
}
