using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class Avis
{
    [Key]
    public int Id { get; set; }

    public int ProduitId { get; set; }
    public virtual Produit Produit { get; set; } = null!;

    public int UtilisateurId { get; set; }
    public virtual Utilisateur Utilisateur { get; set; } = null!;

    [Range(1, 5)]
    public int Note { get; set; }

    [MaxLength(1000)]
    public string Commentaire { get; set; } = string.Empty;

    public string AnalyseSentiment { get; set; } = "Neutre";

    public bool EstValide { get; set; } = false;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
}
