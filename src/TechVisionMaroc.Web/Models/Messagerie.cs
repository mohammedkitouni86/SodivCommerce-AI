using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public enum StatutConversation
{
    Ouverte   = 0,
    EnAttente = 1, // attend réponse admin
    Fermee    = 2,
    Archivee  = 3
}

public class Conversation
{
    public int Id { get; set; }

    public int UtilisateurId { get; set; }
    public virtual Utilisateur? Utilisateur { get; set; }

    [Required, MaxLength(200)]
    public string Sujet { get; set; } = "Question";

    public StatutConversation Statut { get; set; } = StatutConversation.Ouverte;

    /// <summary>Lien optionnel vers une commande / un produit.</summary>
    public int? CommandeId { get; set; }
    public int? ProduitId { get; set; }

    public DateTime DateCreation { get; set; } = DateTime.UtcNow;
    public DateTime DateDernierMessage { get; set; } = DateTime.UtcNow;

    /// <summary>Marqueur pour notifications côté client / admin.</summary>
    public bool LuParClient { get; set; } = true;
    public bool LuParAdmin  { get; set; } = false;

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}

public enum ExpediteurMessage
{
    Client = 0,
    Admin  = 1,
    Systeme = 2
}

public class Message
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public virtual Conversation? Conversation { get; set; }

    public ExpediteurMessage Expediteur { get; set; }

    [Required, MaxLength(4000)]
    public string Contenu { get; set; } = string.Empty;

    public DateTime DateEnvoi { get; set; } = DateTime.UtcNow;
}
