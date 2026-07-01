using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechVisionMaroc.Models;

public class Produit
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Nom { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "decimal(10,2)")]
    public decimal Prix { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? PrixPromo { get; set; }

    /// <summary>Taux de TVA applicable (Maroc : 20% standard, 14% transport, 10% restauration, 7% produits essentiels).</summary>
    [Column(TypeName = "decimal(5,2)")]
    public decimal TauxTVA { get; set; } = 20m;

    public int Stock { get; set; }

    public string ImageUrl { get; set; } = string.Empty;

    // Galerie : images supplémentaires (ordre 2, 3, 4). Photo 1 = ImageUrl.
    [MaxLength(500)]
    public string? Image2 { get; set; }
    [MaxLength(500)]
    public string? Image3 { get; set; }
    [MaxLength(500)]
    public string? Image4 { get; set; }

    /// <summary>Galerie ordonnée (photo principale + images supplémentaires non vides).</summary>
    [NotMapped]
    public List<string> Galerie => new[] { ImageUrl, Image2, Image3, Image4 }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s!)
        .ToList();

    public string Marque { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public bool EstActif { get; set; } = true;

    public bool EstVedette { get; set; } = false;

    public double NoteMoyenne { get; set; } = 0;

    public int NombreAvis { get; set; } = 0;

    public int NombreVentes { get; set; } = 0;

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;

    public int CategorieId { get; set; }
    public virtual Categorie Categorie { get; set; } = null!;

    public virtual ICollection<Avis> Avis { get; set; } = new List<Avis>();
    public virtual ICollection<LigneCommande> LignesCommande { get; set; } = new List<LigneCommande>();
    public virtual ICollection<LignePanier> LignesPanier { get; set; } = new List<LignePanier>();
}
