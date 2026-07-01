using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechVisionMaroc.Models;

public enum StatutCommande
{
    EnAttente,
    Confirmee,
    EnPreparation,
    Expediee,
    Livree,
    Annulee,
    Remboursee
}

public enum MethodePaiement
{
    Stripe,
    CMI,
    PayPal,
    Especes
}

public enum Transporteur
{
    Aucun = 0,
    Amana = 1,
    CTMExpress = 2,
    DHL = 3,
    Autre = 99
}

public class Commande
{
    [Key]
    public int Id { get; set; }

    public string NumeroCommande { get; set; } = string.Empty;

    public int UtilisateurId { get; set; }
    public virtual Utilisateur Utilisateur { get; set; } = null!;

    [Column(TypeName = "decimal(10,2)")]
    public decimal SousTotal { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal FraisLivraison { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Total { get; set; }

    /// <summary>Points fidélité utilisés (1 point = 0,10 MAD de réduction).</summary>
    public int PointsUtilises { get; set; } = 0;

    /// <summary>Réduction en MAD obtenue grâce aux points fidélité.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal ReductionFidelite { get; set; } = 0m;

    /// <summary>Points fidélité crédités à la validation du paiement.</summary>
    public int PointsGagnes { get; set; } = 0;

    public StatutCommande Statut { get; set; } = StatutCommande.EnAttente;

    public MethodePaiement MethodePaiement { get; set; }

    public string? StripePaymentIntentId { get; set; }

    public string AdresseLivraison { get; set; } = string.Empty;

    public string VilleLivraison { get; set; } = string.Empty;

    public string TelephoneLivraison { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime DateCommande { get; set; } = DateTime.UtcNow;

    public DateTime? DateLivraison { get; set; }

    // ── Suivi colis ───────────────────────────────────────────────────────────
    public Transporteur Transporteur { get; set; } = Transporteur.Aucun;
    public string? NumeroSuivi { get; set; }
    public DateTime? DateExpedition { get; set; }

    public virtual ICollection<LigneCommande> Lignes { get; set; } = new List<LigneCommande>();
}

public class LigneCommande
{
    [Key]
    public int Id { get; set; }

    public int CommandeId { get; set; }
    public virtual Commande Commande { get; set; } = null!;

    public int ProduitId { get; set; }
    public virtual Produit Produit { get; set; } = null!;

    public int Quantite { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal PrixUnitaire { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal SousTotal => Quantite * PrixUnitaire;
}
