namespace TechVisionMaroc.Models;

/// <summary>
/// 11 segments standards de l'analyse RFM (Recency, Frequency, Monetary).
/// </summary>
public enum SegmentClient
{
    /// <summary>R 4-5, F 4-5, M 4-5 — meilleurs clients, achètent souvent et récemment.</summary>
    Champions = 1,
    /// <summary>R 3-5, F 3-5, M 3-5 — clients réguliers fidèles.</summary>
    Loyaux = 2,
    /// <summary>R 4-5, F 1-3, M 1-3 — clients récents à fidéliser.</summary>
    PotentielsFideles = 3,
    /// <summary>R 4-5, F 1, M 1 — nouveaux clients.</summary>
    NouveauxClients = 4,
    /// <summary>R 3-4, F 1, M 1 — clients prometteurs.</summary>
    Prometteurs = 5,
    /// <summary>R 2-3, F 2-3, M 2-3 — besoin d'attention pour ne pas les perdre.</summary>
    AttentionRequise = 6,
    /// <summary>R 2-3, F 1-2, M 1-2 — sur le point d'être inactifs.</summary>
    SurLePointDeDormir = 7,
    /// <summary>R 1-2, F 2-5, M 2-5 — clients fidèles devenus inactifs, à reconquérir.</summary>
    ARisque = 8,
    /// <summary>R 1, F 4-5, M 4-5 — anciens VIP perdus de vue.</summary>
    AnciensVIP = 9,
    /// <summary>R 1-2, F 1-2, M 1-2 — endormis depuis longtemps.</summary>
    Endormis = 10,
    /// <summary>R 1, F 1, M 1 — clients perdus.</summary>
    Perdus = 11
}

public class ScoreRFM
{
    public int Id { get; set; }
    public int UtilisateurId { get; set; }
    public Utilisateur? Utilisateur { get; set; }

    /// <summary>Jours depuis le dernier achat.</summary>
    public int JoursDepuisDernierAchat { get; set; }
    /// <summary>Nombre total de commandes valides.</summary>
    public int NombreCommandes { get; set; }
    /// <summary>Total dépensé en MAD.</summary>
    public decimal MontantTotal { get; set; }

    /// <summary>Score Recency (1-5, 5 = très récent).</summary>
    public int ScoreR { get; set; }
    /// <summary>Score Frequency (1-5, 5 = très fréquent).</summary>
    public int ScoreF { get; set; }
    /// <summary>Score Monetary (1-5, 5 = très dépensier).</summary>
    public int ScoreM { get; set; }

    public SegmentClient Segment { get; set; }
    public DateTime DateCalcul { get; set; } = DateTime.UtcNow;
}
