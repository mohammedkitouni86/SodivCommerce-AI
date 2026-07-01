using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum TypeTransactionFidelite
{
    Gagne,    // Points crédités suite à une commande
    Utilise,  // Points dépensés sur une commande
    Expire,   // Points expirés (12 mois sans utilisation)
    Bonus,    // Bonus admin (anniversaire, parrainage…)
    Annule    // Annulation suite à remboursement
}

public class TransactionFidelite
{
    [Key]
    public int Id { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public TypeTransactionFidelite Type { get; set; }

    /// <summary>Points positifs (gagnés/bonus) ou négatifs (utilisés/expirés).</summary>
    public int Points { get; set; }

    public int? CommandeId { get; set; }
    public virtual Commande? Commande { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;

    /// <summary>Date d'expiration des points (Type=Gagne uniquement).</summary>
    public DateTime? DateExpiration { get; set; }
}
