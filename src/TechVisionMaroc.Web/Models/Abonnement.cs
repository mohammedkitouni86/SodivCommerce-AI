using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum FrequenceAbonnement
{
    Hebdo15      = 0,  // tous les 15 jours
    Mensuel      = 1,  // 30 jours
    Bimestriel   = 2,  // 60 jours
    Trimestriel  = 3   // 90 jours
}

public enum StatutAbonnement
{
    Actif    = 0,
    Pause    = 1,
    Annule   = 2
}

public class Abonnement
{
    public int Id { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    public int ProduitId { get; set; }
    public virtual Produit? Produit { get; set; }

    public int Quantite { get; set; } = 1;
    public FrequenceAbonnement Frequence { get; set; } = FrequenceAbonnement.Mensuel;
    public StatutAbonnement Statut { get; set; } = StatutAbonnement.Actif;

    /// <summary>Réduction en % pour avoir souscrit l'abonnement (ex: 5%).</summary>
    public decimal RemiseAbonnement { get; set; } = 5m;

    /// <summary>Adresse de livraison (référence carnet d'adresses).</summary>
    public int? AdresseId { get; set; }

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime ProchaineLivraison { get; set; } = DateTime.UtcNow.AddDays(30);
    public DateTime? DerniereLivraison { get; set; }

    public int NbCommandesGenerees { get; set; } = 0;
}
