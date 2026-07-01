using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechVisionMaroc.Models;

public class Panier
{
    [Key]
    public int Id { get; set; }

    public int? UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public string? SessionId { get; set; }

    public DateTime DateMiseAJour { get; set; } = DateTime.UtcNow;

    public virtual ICollection<LignePanier> Lignes { get; set; } = new List<LignePanier>();

    [NotMapped]
    public decimal Total => Lignes.Sum(l => l.SousTotal);

    [NotMapped]
    public int NombreArticles => Lignes.Sum(l => l.Quantite);
}

public class LignePanier
{
    [Key]
    public int Id { get; set; }

    public int PanierId { get; set; }
    public virtual Panier Panier { get; set; } = null!;

    public int ProduitId { get; set; }
    public virtual Produit Produit { get; set; } = null!;

    public int Quantite { get; set; }

    [NotMapped]
    public decimal SousTotal => Quantite * (Produit?.PrixPromo ?? Produit?.Prix ?? 0);
}
