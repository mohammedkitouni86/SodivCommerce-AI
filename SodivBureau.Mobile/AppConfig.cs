namespace SodivBureau.Mobile;

/// <summary>
/// Configuration centralisée de l'application mobile SODIV.
/// Modifie l'URL ci-dessous pour pointer vers ton serveur (ngrok / production).
/// </summary>
public static class AppConfig
{
    // ═══════════════════════════════════════════════════════════════════════
    //  URL DU SITE SODIV BUREAU
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  💡 Comment obtenir ton URL ngrok :
    //  1. Lance ton site ASP.NET : `dotnet run` (port 5000)
    //  2. Dans un autre terminal : `ngrok http 5000`
    //  3. Copie l'URL "Forwarding https://..." qui s'affiche
    //  4. Colle-la ci-dessous (sans slash final)
    //
    //  Exemples :
    //  - Ngrok (recommandé)    : "https://abc-123-def.ngrok-free.app"
    //  - LAN local Wi-Fi       : "http://192.168.1.10:5000"
    //  - Production            : "https://www.sodibureau.ma"
    //  - Émulateur Android     : "http://10.0.2.2:5000"
    //
    public const string SiteUrl = "https://wildcard-drippy-banker.ngrok-free.dev"; // ✅ ngrok actif

    // ═══════════════════════════════════════════════════════════════════════
    //  Identité de l'app
    // ═══════════════════════════════════════════════════════════════════════
    public const string AppName    = "SODIV Bureau";
    public const string AppTagline = "Tout pour le bureau";
    public const string AppVersion = "1.0.0";

    // ═══════════════════════════════════════════════════════════════════════
    //  Couleurs (synchronisées avec le site web)
    // ═══════════════════════════════════════════════════════════════════════
    public const string PrimaryColor   = "#0d6efd";
    public const string SecondaryColor = "#6610f2";
    public const string AccentColor    = "#f59e0b";
}
