namespace SodivBureau.Mobile.Pages;

public partial class SodivWebPage : ContentPage
{
    private static readonly string SODIV_URL = AppConfig.SiteUrl;

    public SodivWebPage()
    {
        InitializeComponent();
        webView.Source = SODIV_URL;

        // Intercepte le bouton retour Android pour naviguer dans le WebView
#if ANDROID
        // Géré via WebView_Navigated
#endif
    }

    private void WebView_Navigating(object? sender, WebNavigatingEventArgs e)
    {
        loadingLabel.Text = $"Chargement de {GetHost(e.Url)}...";
    }

    private async void WebView_Navigated(object? sender, WebNavigatedEventArgs e)
    {
        // Cache le splash après le 1er chargement réussi
        if (splashScreen.IsVisible && e.Result == WebNavigationResult.Success)
        {
            await splashScreen.FadeTo(0, 500);
            splashScreen.IsVisible = false;
        }

        // Affiche le bouton retour si on n'est pas sur la home
        btnBack.IsVisible = e.Url != SODIV_URL && e.Url != SODIV_URL + "/";

        // Erreur de chargement (URL injoignable)
        if (e.Result == WebNavigationResult.Failure)
        {
            loadingLabel.Text = "⚠️ Impossible de joindre le site.\nVérifie l'URL ou ta connexion.";
        }
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        if (webView.CanGoBack)
        {
            webView.GoBack();
        }
        else
        {
            webView.Source = SODIV_URL;
        }
    }

    // Intercepte le bouton retour matériel (Android)
    protected override bool OnBackButtonPressed()
    {
        if (webView.CanGoBack)
        {
            webView.GoBack();
            return true; // empêche la sortie de l'app
        }
        return base.OnBackButtonPressed();
    }

    private static string GetHost(string url)
    {
        try { return new Uri(url).Host; }
        catch { return url; }
    }
}
