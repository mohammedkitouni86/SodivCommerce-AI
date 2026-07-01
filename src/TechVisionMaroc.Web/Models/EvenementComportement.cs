using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum TypeEvenement
{
    PageVue = 0,
    ProduitVu = 1,
    RechercheSubmit = 2,
    AjoutPanier = 3,
    RetirePanier = 4,
    AjoutWishlist = 5,
    RetireWishlist = 6,
    CheckoutDemarre = 7,
    CommandePassee = 8,
    AjoutComparateur = 9,
    ClicCategorie = 10,
    ScrollProfondeur = 11,
    DureePage = 12,
    FiltreApplique = 13,
    Inscription = 14,
    Connexion = 15,
    Deconnexion = 16
}

public enum TypeAppareil
{
    Inconnu = 0,
    Mobile = 1,
    Tablette = 2,
    Ordinateur = 3,
    Bot = 4
}

/// <summary>
/// Événement comportemental persistant (tracking enrichi).
/// </summary>
public class EvenementComportement
{
    [Key] public long Id { get; set; }

    public int? UtilisateurId { get; set; }

    [MaxLength(80)]  public string? SessionId { get; set; }

    public TypeEvenement Type { get; set; }

    /// <summary>Type de la cible (Produit, Categorie, Commande, …).</summary>
    [MaxLength(40)] public string? CibleType { get; set; }
    public int? CibleId { get; set; }

    [MaxLength(300)] public string? Page { get; set; }
    [MaxLength(300)] public string? Referer { get; set; }
    [MaxLength(500)] public string? UserAgent { get; set; }
    [MaxLength(45)]  public string? IpAdresse { get; set; }
    public TypeAppareil Appareil { get; set; } = TypeAppareil.Inconnu;

    /// <summary>Durée (ms) pour les événements DureePage / ScrollProfondeur.</summary>
    public int? DureeMs { get; set; }

    /// <summary>Valeur numérique (montant, profondeur scroll en %, ...).</summary>
    public decimal? Valeur { get; set; }

    /// <summary>Texte libre (requête de recherche, code filtre, ...).</summary>
    [MaxLength(300)] public string? Etiquette { get; set; }

    /// <summary>Métadonnées JSON additionnelles.</summary>
    [MaxLength(1000)] public string? Metadonnees { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;
}
