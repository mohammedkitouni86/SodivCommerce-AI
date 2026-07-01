using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum StatutDevis
{
    EnAttente = 0,   // attendu réponse admin
    Repondu   = 1,   // admin a répondu, attente client
    Accepte   = 2,   // client accepte → commande créée
    Refuse    = 3,   // client refuse
    Expire    = 4    // sans réponse > 30 jours
}

public class Devis
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string NumeroDevis { get; set; } = string.Empty;  // ex: "DEV-2026-00001"

    public int? UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    /// <summary>Coordonnées du demandeur (si pas connecté ou pour entreprise).</summary>
    [Required, MaxLength(200)] public string ContactNom { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContactEmail { get; set; } = string.Empty;
    [Required, MaxLength(20)]  public string ContactTelephone { get; set; } = string.Empty;
    [MaxLength(150)]           public string? Entreprise { get; set; }
    [MaxLength(80)]            public string? VilleLivraison { get; set; }

    [Required, MaxLength(2000)]
    public string DemandeClient { get; set; } = string.Empty;

    public StatutDevis Statut { get; set; } = StatutDevis.EnAttente;

    /// <summary>Réponse de l'admin (devis chiffré).</summary>
    [MaxLength(4000)]
    public string? ReponseAdmin { get; set; }

    /// <summary>Total proposé par l'admin.</summary>
    public decimal? MontantPropose { get; set; }

    /// <summary>Délai de livraison promis (en jours).</summary>
    public int? DelaiLivraisonJours { get; set; }

    /// <summary>Validité du devis (jours).</summary>
    public int? ValiditeJours { get; set; } = 30;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime? DateReponse { get; set; }
    public DateTime? DateDecision { get; set; }
    public DateTime? DateExpiration { get; set; }

    /// <summary>Si accepté, ID de la commande créée.</summary>
    public int? CommandeId { get; set; }

    public virtual ICollection<LigneDevis> Lignes { get; set; } = new List<LigneDevis>();
}

public class LigneDevis
{
    public int Id { get; set; }
    public int DevisId { get; set; }
    public virtual Devis? Devis { get; set; }

    /// <summary>Optionnel : produit catalogue (sinon ligne libre).</summary>
    public int? ProduitId { get; set; }
    public virtual Produit? Produit { get; set; }

    [Required, MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    public int Quantite { get; set; } = 1;

    /// <summary>Prix unitaire proposé par l'admin (null si pas encore chiffré).</summary>
    public decimal? PrixUnitaire { get; set; }
}
