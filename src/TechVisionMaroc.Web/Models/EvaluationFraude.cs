using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TechVisionMaroc.Models;

public enum NiveauRisque
{
    Faible = 0,
    Modere = 1,
    Eleve = 2,
    Critique = 3
}

public enum DecisionFraude
{
    EnAttente = 0,
    Validee = 1,
    Bloquee = 2,
    AReviser = 3
}

/// <summary>
/// Évaluation du risque de fraude pour une commande (calculée à la création).
/// </summary>
public class EvaluationFraude
{
    [Key] public int Id { get; set; }

    public int CommandeId { get; set; }
    public virtual Commande Commande { get; set; } = null!;

    public int UtilisateurId { get; set; }

    /// <summary>Score 0–100 (0 = sûr, 100 = très suspect).</summary>
    public int Score { get; set; }

    public NiveauRisque Niveau { get; set; } = NiveauRisque.Faible;

    public DecisionFraude Decision { get; set; } = DecisionFraude.EnAttente;

    /// <summary>JSON : liste des règles déclenchées (Code, Description, Poids).</summary>
    [MaxLength(2000)]
    public string? Raisons { get; set; }

    [MaxLength(45)]
    public string? IpAdresse { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime DateEvaluation { get; set; } = DateTime.UtcNow;

    public DateTime? DateRevision { get; set; }
    public int? ReviseParId { get; set; }

    [MaxLength(300)]
    public string? CommentaireRevision { get; set; }
}
