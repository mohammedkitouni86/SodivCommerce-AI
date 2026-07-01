using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechVisionMaroc.Models;

/// <summary>Liste de souhaits — un produit favori par utilisateur, avec alerte baisse de prix optionnelle.</summary>
public class Wishlist
{
    [Key]
    public int Id { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public int ProduitId { get; set; }
    public virtual Produit? Produit { get; set; }

    /// <summary>Prix TTC du produit au moment de l'ajout — sert de référence pour détecter une baisse.</summary>
    [Column(TypeName = "decimal(10,2)")]
    public decimal PrixReference { get; set; }

    /// <summary>L'utilisateur veut-il être notifié par email si le prix baisse ?</summary>
    public bool AlertePrix { get; set; } = true;

    /// <summary>Date du dernier email d'alerte (pour ne pas spammer).</summary>
    public DateTime? DerniereAlerte { get; set; }

    public DateTime DateAjout { get; set; } = DateTime.UtcNow;
}
