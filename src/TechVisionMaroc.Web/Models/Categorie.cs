using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class Categorie
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Nom { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string IconeClass { get; set; } = "fa-box";

    /// <summary>URL de l'image de la catégorie (utilisée dans le mega-menu et page catégorie).</summary>
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool EstActive { get; set; } = true;

    public int? ParentId { get; set; }
    public virtual Categorie? Parent { get; set; }

    public virtual ICollection<Produit> Produits { get; set; } = new List<Produit>();
    public virtual ICollection<Categorie> SousCategories { get; set; } = new List<Categorie>();
}
