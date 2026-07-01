using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Net.Mail;

namespace TechVisionMaroc.Controllers;

public class DevisController : Controller
{
    private readonly IConfiguration _config;

    public DevisController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("/Devis")]
    public IActionResult Index() => View();

    [HttpPost("/Devis/Envoyer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Envoyer(
        string nomProduit, int produitId,
        string nom, string? societe,
        string email, string? telephone,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(nom) || string.IsNullOrWhiteSpace(email))
            return Json(new { ok = false, erreur = "Nom et email sont obligatoires." });

        try
        {
            var cfg         = _config.GetSection("Email");
            var smtpHost    = cfg["SmtpHost"]      ?? "smtp.gmail.com";
            var smtpPort    = int.Parse(cfg["SmtpPort"] ?? "587");
            var adresse     = cfg["AdresseGmail"]  ?? "";
            var motDePasse  = cfg["MotDePasseApp"] ?? "";
            var destinataire= cfg["Destinataire"]  ?? adresse;
            var expediteur  = cfg["Expediteur"]    ?? adresse;

            var corps = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:auto'>
  <div style='background:#0d6efd;color:white;padding:20px 30px;border-radius:8px 8px 0 0'>
    <h2 style='margin:0'>Nouvelle demande de devis</h2>
    <p style='margin:5px 0 0;opacity:.85'>SODIV Bureau — {DateTime.Now:dd/MM/yyyy HH:mm}</p>
  </div>
  <div style='background:#f8f9fa;padding:25px 30px;border-radius:0 0 8px 8px;border:1px solid #dee2e6'>
    <table style='width:100%;border-collapse:collapse'>
      <tr><td style='padding:8px 0;color:#6c757d;width:140px'>Produit</td>
          <td style='padding:8px 0;font-weight:bold;color:#212529'>{System.Web.HttpUtility.HtmlEncode(nomProduit)}</td></tr>
      <tr><td style='padding:8px 0;color:#6c757d'>Nom</td>
          <td style='padding:8px 0'>{System.Web.HttpUtility.HtmlEncode(nom)}</td></tr>
      {(string.IsNullOrWhiteSpace(societe) ? "" : $"<tr><td style='padding:8px 0;color:#6c757d'>Société</td><td style='padding:8px 0'>{System.Web.HttpUtility.HtmlEncode(societe)}</td></tr>")}
      <tr><td style='padding:8px 0;color:#6c757d'>Email</td>
          <td style='padding:8px 0'><a href='mailto:{System.Web.HttpUtility.HtmlEncode(email)}'>{System.Web.HttpUtility.HtmlEncode(email)}</a></td></tr>
      {(string.IsNullOrWhiteSpace(telephone) ? "" : $"<tr><td style='padding:8px 0;color:#6c757d'>Téléphone</td><td style='padding:8px 0'>{System.Web.HttpUtility.HtmlEncode(telephone)}</td></tr>")}
    </table>
    {(string.IsNullOrWhiteSpace(message) ? "" : $@"
    <div style='margin-top:20px'>
      <div style='color:#6c757d;margin-bottom:6px'>Message</div>
      <div style='background:white;border:1px solid #dee2e6;border-radius:6px;padding:14px;white-space:pre-wrap'>{System.Web.HttpUtility.HtmlEncode(message)}</div>
    </div>")}
    <div style='margin-top:24px;padding-top:16px;border-top:1px solid #dee2e6;text-align:center;color:#6c757d;font-size:13px'>
      SODIV Bureau — sodivbureau@gmail.com
    </div>
  </div>
</div>";

            using var smtp = new SmtpClient(smtpHost, smtpPort)
            {
                Credentials    = new NetworkCredential(adresse, motDePasse),
                EnableSsl      = true,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            using var mail = new MailMessage
            {
                From       = new MailAddress(adresse, "SODIV Bureau"),
                Subject    = $"Devis — {nomProduit} ({nom})",
                Body       = corps,
                IsBodyHtml = true
            };
            mail.To.Add(destinataire);
            mail.ReplyToList.Add(new MailAddress(email, nom));

            await smtp.SendMailAsync(mail);

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, erreur = $"Erreur d'envoi : {ex.Message}" });
        }
    }
}
