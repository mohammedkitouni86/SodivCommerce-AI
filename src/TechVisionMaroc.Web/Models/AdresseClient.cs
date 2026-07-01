using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class AdresseClient
{
    public int Id { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    [Required, MaxLength(80)]
    public string Libelle { get; set; } = "Adresse";  // ex: "Bureau principal", "Siège social"

    [Required, MaxLength(100)] public string DestinataireNom { get; set; } = string.Empty;
    [Required, MaxLength(20)]  public string Telephone { get; set; } = string.Empty;
    [Required, MaxLength(250)] public string Adresse { get; set; } = string.Empty;
    [Required, MaxLength(80)]  public string Ville { get; set; } = string.Empty;
    [MaxLength(20)]            public string? CodePostal { get; set; }
    [MaxLength(80)]            public string? Pays { get; set; } = "Maroc";
    [MaxLength(300)]           public string? Complement { get; set; }  // étage, code porte, etc.

    public bool ParDefaut { get; set; } = false;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
}
