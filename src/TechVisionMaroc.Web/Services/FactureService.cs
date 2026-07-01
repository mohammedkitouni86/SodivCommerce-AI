using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TechVisionMaroc.Data;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Services;

public interface IFactureService
{
    Task<byte[]?> GenererPdfAsync(int commandeId);
    string GenererNumeroFacture(int commandeId, DateTime date);
}

public class FactureService : IFactureService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public FactureService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public string GenererNumeroFacture(int commandeId, DateTime date) =>
        $"FAC-{date.Year}-{commandeId:D5}";

    public async Task<byte[]?> GenererPdfAsync(int commandeId)
    {
        var c = await _db.Commandes
            .Include(x => x.Utilisateur)
            .Include(x => x.Lignes).ThenInclude(l => l.Produit)
            .FirstOrDefaultAsync(x => x.Id == commandeId);
        if (c == null) return null;

        var soc = _config.GetSection("Societe");
        var tauxTVA = decimal.TryParse(soc["TauxTVA"], out var t) ? t : 20m;
        var totalTTC = c.Total;
        var totalHT  = Math.Round(totalTTC / (1 + tauxTVA / 100), 2);
        var tvaMontant = Math.Round(totalTTC - totalHT, 2);
        var numFacture = GenererNumeroFacture(c.Id, c.DateCommande);

        var pdfBytes = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Margin(30);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(s => s.FontSize(10).FontFamily("Arial"));

                // EN-TÊTE
                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(soc["Nom"] ?? "Société").FontSize(20).Bold().FontColor(Colors.Blue.Darken3);
                        col.Item().Text(soc["Adresse"] ?? "").FontSize(9);
                        col.Item().Text($"{soc["CodePostal"]} {soc["Ville"]}, {soc["Pays"]}").FontSize(9);
                        col.Item().Text($"Tél : {soc["Telephone"]}").FontSize(9);
                        col.Item().Text($"Email : {soc["Email"]}").FontSize(9);
                        col.Item().Text($"Site : {soc["SiteWeb"]}").FontSize(9);
                    });

                    row.ConstantItem(200).Column(col =>
                    {
                        col.Item().AlignRight().Text("FACTURE").FontSize(28).Bold().FontColor(Colors.Blue.Darken3);
                        col.Item().AlignRight().Text(numFacture).FontSize(12).SemiBold();
                        col.Item().PaddingTop(8).AlignRight().Text($"Date : {c.DateCommande:dd/MM/yyyy}");
                        col.Item().AlignRight().Text($"Commande : {c.NumeroCommande}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });

                page.Content().PaddingVertical(15).Column(col =>
                {
                    // CLIENT
                    col.Item().PaddingBottom(15).Background(Colors.Grey.Lighten4).Padding(10).Column(cc =>
                    {
                        cc.Item().Text("FACTURÉ À").FontSize(9).Bold().FontColor(Colors.Grey.Darken2);
                        cc.Item().PaddingTop(4).Text($"{c.Utilisateur.Prenom} {c.Utilisateur.Nom}").Bold();
                        cc.Item().Text(c.AdresseLivraison).FontSize(9);
                        cc.Item().Text(c.VilleLivraison).FontSize(9);
                        cc.Item().Text($"Tél : {c.TelephoneLivraison}").FontSize(9);
                        cc.Item().Text($"Email : {c.Utilisateur.Email}").FontSize(9);
                    });

                    // LIGNES
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c2 =>
                        {
                            c2.ConstantColumn(30);
                            c2.RelativeColumn(5);
                            c2.ConstantColumn(50);
                            c2.ConstantColumn(80);
                            c2.ConstantColumn(80);
                        });

                        table.Header(h =>
                        {
                            static IContainer Cell(IContainer x) => x.DefaultTextStyle(s => s.SemiBold().FontColor(Colors.White)).Background(Colors.Blue.Darken3).Padding(6);
                            h.Cell().Element(Cell).Text("#");
                            h.Cell().Element(Cell).Text("Désignation");
                            h.Cell().Element(Cell).AlignRight().Text("Qté");
                            h.Cell().Element(Cell).AlignRight().Text("P.U. HT");
                            h.Cell().Element(Cell).AlignRight().Text("Total HT");
                        });

                        int i = 1;
                        foreach (var l in c.Lignes)
                        {
                            var puHT  = Math.Round(l.PrixUnitaire / (1 + tauxTVA / 100), 2);
                            var totHT = Math.Round(puHT * l.Quantite, 2);
                            static IContainer Body(IContainer x) => x.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(6);
                            table.Cell().Element(Body).Text(i.ToString());
                            table.Cell().Element(Body).Text(l.Produit?.Nom ?? $"Produit #{l.ProduitId}");
                            table.Cell().Element(Body).AlignRight().Text(l.Quantite.ToString());
                            table.Cell().Element(Body).AlignRight().Text($"{puHT:N2}");
                            table.Cell().Element(Body).AlignRight().Text($"{totHT:N2}");
                            i++;
                        }
                    });

                    // TOTAUX
                    col.Item().PaddingTop(10).AlignRight().Width(250).Column(t2 =>
                    {
                        t2.Item().Row(r => { r.RelativeItem().Text("Total HT").SemiBold(); r.ConstantItem(90).AlignRight().Text($"{totalHT:N2} MAD"); });
                        t2.Item().Row(r => { r.RelativeItem().Text($"TVA ({tauxTVA:N0}%)").SemiBold(); r.ConstantItem(90).AlignRight().Text($"{tvaMontant:N2} MAD"); });
                        if (c.FraisLivraison > 0)
                            t2.Item().Row(r => { r.RelativeItem().Text("Frais livraison"); r.ConstantItem(90).AlignRight().Text($"{c.FraisLivraison:N2} MAD"); });
                        t2.Item().PaddingTop(4).BorderTop(1).PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Text("TOTAL TTC").Bold().FontSize(13).FontColor(Colors.Blue.Darken3);
                            r.ConstantItem(90).AlignRight().Text($"{totalTTC:N2} MAD").Bold().FontSize(13).FontColor(Colors.Blue.Darken3);
                        });
                    });

                    // Arrêté en lettres
                    col.Item().PaddingTop(15).Text(t2 =>
                    {
                        t2.Span("Arrêté la présente facture à la somme de : ").Italic();
                        t2.Span(MontantEnLettres(totalTTC)).Italic().Bold();
                    });

                    col.Item().PaddingTop(10).Text($"Méthode de paiement : {c.MethodePaiement}").FontSize(9);
                    col.Item().Text($"Statut : {c.Statut}").FontSize(9);
                });

                // PIED — Mentions légales DGI Maroc
                page.Footer().Column(col =>
                {
                    col.Item().BorderTop(1).BorderColor(Colors.Grey.Lighten1).PaddingTop(6);
                    col.Item().AlignCenter().Text(t2 =>
                    {
                        t2.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                        t2.Span($"{soc["Nom"]} — ").SemiBold();
                        t2.Span($"ICE : {soc["ICE"]} · ");
                        t2.Span($"RC : {soc["RC"]} · ");
                        t2.Span($"IF : {soc["IF"]} · ");
                        t2.Span($"TP : {soc["TP"]} · ");
                        t2.Span($"CNSS : {soc["CNSS"]} · ");
                        t2.Span($"Capital : {soc["Capital"]} MAD");
                    });
                    col.Item().AlignCenter().Text($"{soc["Adresse"]} · {soc["CodePostal"]} {soc["Ville"]} · {soc["Telephone"]}")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

        return pdfBytes;
    }

    // Conversion basique montant → texte (français/dirham marocain)
    private static string MontantEnLettres(decimal montant)
    {
        var entier = (long)Math.Floor(montant);
        var centimes = (int)Math.Round((montant - entier) * 100);
        var txt = NombreEnLettres(entier) + " dirham" + (entier > 1 ? "s" : "");
        if (centimes > 0)
            txt += " et " + NombreEnLettres(centimes) + " centime" + (centimes > 1 ? "s" : "");
        return char.ToUpper(txt[0]) + txt.Substring(1) + " TTC.";
    }

    private static readonly string[] Unites = { "zéro","un","deux","trois","quatre","cinq","six","sept","huit","neuf",
        "dix","onze","douze","treize","quatorze","quinze","seize","dix-sept","dix-huit","dix-neuf" };
    private static readonly string[] Dizaines = { "","","vingt","trente","quarante","cinquante","soixante","soixante","quatre-vingt","quatre-vingt" };

    private static string NombreEnLettres(long n)
    {
        if (n < 0) return "moins " + NombreEnLettres(-n);
        if (n < 20) return Unites[n];
        if (n < 100)
        {
            var d = n / 10; var u = n % 10;
            if (d == 7 || d == 9) return Dizaines[d] + "-" + Unites[10 + u];
            if (u == 0) return Dizaines[d] + (d == 8 ? "s" : "");
            if (u == 1 && d != 8) return Dizaines[d] + " et un";
            return Dizaines[d] + "-" + Unites[u];
        }
        if (n < 1000)
        {
            var c = n / 100; var r = n % 100;
            var prefix = c == 1 ? "cent" : NombreEnLettres(c) + " cent" + (r == 0 ? "s" : "");
            return r == 0 ? prefix : prefix + " " + NombreEnLettres(r);
        }
        if (n < 1_000_000)
        {
            var m = n / 1000; var r = n % 1000;
            var prefix = m == 1 ? "mille" : NombreEnLettres(m) + " mille";
            return r == 0 ? prefix : prefix + " " + NombreEnLettres(r);
        }
        var mi = n / 1_000_000; var rr = n % 1_000_000;
        var p2 = NombreEnLettres(mi) + " million" + (mi > 1 ? "s" : "");
        return rr == 0 ? p2 : p2 + " " + NombreEnLettres(rr);
    }
}
