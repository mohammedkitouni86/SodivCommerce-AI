using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum TypeReduction
{
    Pourcentage = 0,  // ex: 10%
    Montant     = 1,  // ex: -50 MAD
    LivraisonGratuite = 2
}

public enum TypeCoupon
{
    Bienvenue   = 0,
    Promo       = 1,
    Anniversaire = 2,
    Parrainage  = 3,
    Fidelite    = 4,
    Compensation = 5
}

public class Coupon
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string Code { get; set; } = string.Empty;     // ex: "SODIV10"

    [Required, MaxLength(200)]
    public string Libelle { get; set; } = string.Empty;  // ex: "10% sur tout le catalogue"

    public TypeCoupon Type { get; set; } = TypeCoupon.Promo;
    public TypeReduction TypeReduction { get; set; } = TypeReduction.Pourcentage;

    public decimal Valeur { get; set; }            // 10 (=10% ou 10 MAD)
    public decimal? MontantMinimum { get; set; }   // commande min pour utiliser
    public decimal? ReductionMaximum { get; set; } // plafond en MAD

    public DateTime DateDebut { get; set; } = DateTime.UtcNow;
    public DateTime DateFin   { get; set; } = DateTime.UtcNow.AddMonths(1);

    public int? LimiteUtilisation { get; set; } // max utilisations globales (null = illimité)
    public int  UtilisationActuelle { get; set; } = 0;

    public int? LimiteParClient { get; set; } = 1; // max par client

    public bool EstActif { get; set; } = true;

    /// <summary>Si défini : coupon réservé à 1 client (ex: parrainage).</summary>
    public int? UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
}

public class UtilisationCoupon
{
    public int Id { get; set; }
    public int CouponId { get; set; }
    public virtual Coupon? Coupon { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public int? CommandeId { get; set; }
    public virtual Commande? Commande { get; set; }

    public decimal MontantReduction { get; set; }
    public DateTime DateUtilisation { get; set; } = DateTime.UtcNow;
}
