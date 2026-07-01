using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class Utilisateur
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Prenom { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string MotDePasseHash { get; set; } = string.Empty;

    public string Telephone { get; set; } = string.Empty;

    public string Adresse { get; set; } = string.Empty;

    public string Ville { get; set; } = string.Empty;

    public string Role { get; set; } = "Client";

    public bool EstActif { get; set; } = true;

    public DateTime DateInscription { get; set; } = DateTime.UtcNow;

    public DateTime? DerniereConnexion { get; set; }

    public DateTime? DateNaissance { get; set; }

    public int TentativesEchouees { get; set; } = 0;

    public DateTime? BloqueJusqua { get; set; }

    // 2FA TOTP (Google Authenticator)
    public string? TotpSecret { get; set; }
    public bool TotpActive { get; set; } = false;

    // Points fidélité (1 MAD HT dépensé = 1 point)
    public int PointsFidelite { get; set; } = 0;

    // Parrainage : id du parrain (celui dont le code a été utilisé à l'inscription)
    public int? ParrainId { get; set; }
    // Récompense de parrainage déjà versée (au 1er achat qualifiant du filleul) ?
    public bool ParrainageRecompense { get; set; } = false;

    public virtual ICollection<Commande> Commandes { get; set; } = new List<Commande>();
    public virtual ICollection<Avis> Avis { get; set; } = new List<Avis>();
    public virtual Panier? Panier { get; set; }
}
