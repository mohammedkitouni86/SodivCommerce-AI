namespace TechVisionMaroc.Models;

/// <summary>
/// Score de similarité co-achat entre deux produits.
/// Calculé par <see cref="Services.RecommandationCollaborativeService"/>.
/// </summary>
public class RecommandationProduit
{
    public int Id { get; set; }

    /// <summary>Produit source (celui que le client regarde).</summary>
    public int ProduitId { get; set; }
    public Produit? Produit { get; set; }

    /// <summary>Produit recommandé (acheté souvent avec ProduitId).</summary>
    public int ProduitRecommandeId { get; set; }
    public Produit? ProduitRecommande { get; set; }

    /// <summary>Score Jaccard 0..1 (plus haut = plus pertinent).</summary>
    public double Score { get; set; }

    /// <summary>Nombre de commandes contenant les deux produits.</summary>
    public int CoOccurrences { get; set; }

    public DateTime DateCalcul { get; set; } = DateTime.UtcNow;
}
