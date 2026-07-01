using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using TechVisionMaroc.Data;

namespace TechVisionMaroc.Services;

public interface IChatbotService
{
    Task<ChatbotReponse> RepondreAsync(string message, List<ChatbotMessage> historique);
}

public record ChatbotMessage(string Role, string Content);
public record ChatbotReponse(string Reponse, List<ProduitSuggere> Produits);
public record ProduitSuggere(int Id, string Nom, decimal Prix, string ImageUrl, string Url);

/// <summary>
/// Chatbot RAG : récupère les produits pertinents puis appelle Groq (Llama 3.3).
/// Fonctionne sans clé API en mode "secours" (réponse basique sans IA).
/// </summary>
public class ChatbotService : IChatbotService
{
    private readonly AppDbContext _db;
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ChatbotService> _logger;

    public ChatbotService(AppDbContext db, HttpClient http, IConfiguration config, ILogger<ChatbotService> logger)
    {
        _db = db;
        _http = http;
        _config = config;
        _logger = logger;
    }

    public async Task<ChatbotReponse> RepondreAsync(string message, List<ChatbotMessage> historique)
    {
        // 1) Retrieval : top 5 produits pertinents
        var produits = await RechercherProduitsPertinentsAsync(message);

        // 2) Construire le contexte produits
        var contexteCatalogue = new StringBuilder();
        if (produits.Count > 0)
        {
            contexteCatalogue.AppendLine("Voici les produits du catalogue SODIV Bureau qui correspondent :");
            foreach (var p in produits)
            {
                var prix = (p.PrixPromo ?? p.Prix).ToString("N0");
                contexteCatalogue.AppendLine($"- [Produit #{p.Id}] {p.Nom} ({p.Marque}) — {prix} MAD — Stock: {p.Stock}. {Tronquer(p.Description, 200)}");
            }
        }
        else
        {
            contexteCatalogue.AppendLine("Aucun produit pertinent dans le catalogue pour cette demande.");
        }

        // 3) Prompt système
        var systemPrompt = $$"""
            Tu es SODIVA, assistante virtuelle de SODIV Bureau, e-commerce marocain spécialisé en matériel informatique et fournitures de bureau (Salé, Maroc).
            Tu réponds UNIQUEMENT à partir des produits listés ci-dessous. Tu ne dois JAMAIS inventer de produit absent de cette liste.
            Tu utilises les prix en MAD (dirham marocain).
            Réponds en français, de manière courte (2-4 phrases max), polie et professionnelle.
            Quand tu mentionnes un produit, cite son nom exact.
            Si la question ne concerne pas le catalogue (horaires, livraison, contact), réponds brièvement avec ces infos : livraison Salé/Maroc, paiement à la livraison ou en ligne, contact sodivbureau@gmail.com (téléphone +212 661 900 569).
            Si aucun produit ne correspond, propose poliment d'affiner la recherche.

            CATALOGUE :
            {{contexteCatalogue}}
            """;

        // 4) Appel à Groq (ou fallback)
        var apiKey = _config["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("gsk_VOTRE"))
        {
            return new ChatbotReponse(RéponseSecours(produits, message), MapProduits(produits));
        }

        try
        {
            var messages = new List<object> { new { role = "system", content = systemPrompt } };
            foreach (var h in historique.TakeLast(6))
                messages.Add(new { role = h.Role, content = h.Content });
            messages.Add(new { role = "user", content = message });

            var body = new
            {
                model = _config["Groq:Model"] ?? "llama-3.3-70b-versatile",
                messages,
                temperature = 0.3,
                max_tokens = 350
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req);
            var json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Groq error {Code}: {Body}", resp.StatusCode, json);
                return new ChatbotReponse(RéponseSecours(produits, message), MapProduits(produits));
            }

            using var doc = JsonDocument.Parse(json);
            var reponse = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "Je n'ai pas pu générer de réponse.";

            return new ChatbotReponse(reponse, MapProduits(produits));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec appel Groq");
            return new ChatbotReponse(RéponseSecours(produits, message), MapProduits(produits));
        }
    }

    private async Task<List<Models.Produit>> RechercherProduitsPertinentsAsync(string question)
    {
        // Extraction simple de mots-clés + détection de prix
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "le","la","les","un","une","des","de","du","et","ou","à","au","aux","en","pour","par","avec","sur","dans","comme",
            "je","tu","il","nous","vous","ce","cette","ces","quel","quelle","quels","est","sont","mon","ma","mes","votre","vos",
            "cherche","veux","voudrais","besoin","propose","avez","peux","peut","trouve","cherchez","entre","trouver","voir","avoir","faut",
            "acheter","achete","achète","achat","commander","commande","prix","produit","produits","article","articles",
            "svp","stp","merci","bonjour","salut","salam","montre","montrez","donne","donnez","donner","recherche","cadeau"
        };

        var tokens = question
            .Split(new[] { ' ', ',', '?', '!', '.', ';', ':', '\'', '\"' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 1 && !stop.Contains(t))
            .ToList();

        decimal? prixCible = null;
        foreach (var t in tokens)
        {
            if (decimal.TryParse(t.Replace(",", "."), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n) && n > 50)
                { prixCible = n; break; }
        }

        // Mots de recherche (hors nombres) en minuscules.
        var motsTexte = tokens
            .Where(t => !decimal.TryParse(t, out _))
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToList();

        // Candidats : produits actifs (+ filtre prix éventuel). Catalogue modeste → scoring en mémoire.
        var candidatsQuery = _db.Produits.Include(p => p.Categorie).Where(p => p.EstActif);
        if (prixCible.HasValue)
        {
            var min = prixCible.Value * 0.7m;
            var max = prixCible.Value * 1.3m;
            candidatsQuery = candidatsQuery.Where(p => p.Prix >= min && p.Prix <= max);
        }
        var candidats = await candidatsQuery.ToListAsync();

        // Aucun mot exploitable → on propose les plus populaires (dans la fourchette de prix).
        if (motsTexte.Count == 0)
            return candidats
                .OrderByDescending(p => p.NombreVentes).ThenByDescending(p => p.NoteMoyenne)
                .Take(5).ToList();

        static string Norm(string? s) => (s ?? "").ToLowerInvariant();

        // Score = nombre de mots de la phrase trouvés dans nom / marque / description / catégorie.
        var resultats = candidats
            .Select(p =>
            {
                var foin = $"{Norm(p.Nom)} {Norm(p.Marque)} {Norm(p.Description)} {Norm(p.Categorie?.Nom)}";
                int score = motsTexte.Count(m => foin.Contains(m));
                return (Produit: p, Score: score);
            })
            .Where(x => x.Score > 0)            // au moins UN mot correspond
            .OrderByDescending(x => x.Score)    // plus de mots trouvés = plus pertinent
            .ThenByDescending(x => x.Produit.NombreVentes)
            .ThenByDescending(x => x.Produit.NoteMoyenne)
            .Take(5)
            .Select(x => x.Produit)
            .ToList();

        return resultats;
    }

    private static string RéponseSecours(List<Models.Produit> produits, string question)
    {
        if (produits.Count == 0)
            return "Je n'ai pas trouvé de produit correspondant. Pouvez-vous préciser (marque, type, budget) ?";

        var sb = new StringBuilder();
        sb.AppendLine($"Voici {produits.Count} produit(s) qui pourraient vous intéresser :");
        foreach (var p in produits.Take(3))
            sb.AppendLine($"• {p.Nom} — {(p.PrixPromo ?? p.Prix):N0} MAD");
        sb.Append("Cliquez sur un produit pour plus de détails.");
        return sb.ToString();
    }

    private static List<ProduitSuggere> MapProduits(List<Models.Produit> ps) =>
        ps.Select(p => new ProduitSuggere(
            p.Id, p.Nom, p.PrixPromo ?? p.Prix,
            string.IsNullOrEmpty(p.ImageUrl) ? "/images/placeholder.svg" : p.ImageUrl,
            $"/Produit/Details/{p.Id}")).ToList();

    private static string Tronquer(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s[..n] + "…");
}
