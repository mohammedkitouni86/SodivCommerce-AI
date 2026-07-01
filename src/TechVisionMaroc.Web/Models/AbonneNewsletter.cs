using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class AbonneNewsletter
{
    public int Id { get; set; }

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    public DateTime DateInscription { get; set; } = DateTime.UtcNow;

    public bool EstActif { get; set; } = true;

    public string? Source { get; set; }   // ex : footer, popup, parrainage
}
