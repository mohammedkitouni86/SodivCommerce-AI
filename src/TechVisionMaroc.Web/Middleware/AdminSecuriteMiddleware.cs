namespace TechVisionMaroc.Middleware;

public class AdminSecuriteMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _secretPrefix;
    private readonly string _internalPrefix = "/admin";
    private readonly string _pin;
    private readonly IReadOnlyList<string> _ipsAutorises;

    public AdminSecuriteMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next         = next;
        _secretPrefix = "/" + (config["Admin:RoutePrefix"] ?? "admin").TrimStart('/');
        _pin          = config["Admin:PinCode"] ?? "";
        _ipsAutorises = config.GetSection("Admin:IpsAutorises").Get<string[]>() ?? [];
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";

        // ── Accès direct à /admin : laisser passer si admin/superadmin connecté + PIN ok ──
        if (path.Equals(_internalPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(_internalPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            var role = ctx.Session.GetString("UtilisateurRole");
            var pinOk = string.IsNullOrEmpty(_pin) || ctx.Session.GetString("AdminPinOk") == "1";
            if ((role == "Admin" || role == "SuperAdmin") && pinOk)
            {
                await _next(ctx);
                return;
            }
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("Page introuvable.");
            return;
        }

        // ── Laisser passer tout ce qui n'est pas la zone secrète ────────────
        if (!path.Equals(_secretPrefix, StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith(_secretPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // ── Zone secrète ─────────────────────────────────────────────────────

        // 1. Restriction IP
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        if (_ipsAutorises.Count > 0 && !_ipsAutorises.Contains(ip))
        {
            ctx.Response.StatusCode = 404;
            await ctx.Response.WriteAsync("Page introuvable.");
            return;
        }

        // La page PIN est accessible sans être connecté (pour afficher le formulaire)
        var isPinPage = path.StartsWith(_secretPrefix + "/pin", StringComparison.OrdinalIgnoreCase);

        // 2. Vérification connexion Admin/SuperAdmin (sauf page PIN)
        if (!isPinPage)
        {
            var role = ctx.Session.GetString("UtilisateurRole");
            if (role != "Admin" && role != "SuperAdmin")
            {
                var loginUrl = "/Account/Connexion?returnUrl=" + Uri.EscapeDataString(path);
                ctx.Response.Redirect(loginUrl);
                return;
            }

            // 3. Vérification PIN
            if (!string.IsNullOrEmpty(_pin) && ctx.Session.GetString("AdminPinOk") != "1")
            {
                ctx.Response.Redirect(_secretPrefix + "/pin?returnUrl=" + Uri.EscapeDataString(path));
                return;
            }
        }

        // 4. Réécriture URL : /gestion-tv-9f3k/xxx → /admin/xxx
        var reste = path.Substring(_secretPrefix.Length);
        ctx.Request.Path = _internalPrefix + (string.IsNullOrEmpty(reste) ? "" : reste);

        await _next(ctx);
    }
}
