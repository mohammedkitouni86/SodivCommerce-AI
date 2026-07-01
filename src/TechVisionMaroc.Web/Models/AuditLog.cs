using System.ComponentModel.DataAnnotations;

namespace TechVisionMaroc.Models;

public class AuditLog
{
    [Key]
    public int Id { get; set; }

    public int? UtilisateurId { get; set; }
    public string? UtilisateurEmail { get; set; }
    public string? Role { get; set; }

    [Required, MaxLength(60)]
    public string Action { get; set; } = "";   // ex: LOGIN, LOGOUT, CREATE_PRODUCT, DELETE_USER...

    [MaxLength(200)]
    public string? Cible { get; set; }         // ex: "Produit #42", "Commande FAC-..."

    [MaxLength(500)]
    public string? Details { get; set; }

    [MaxLength(45)]
    public string? IpAdresse { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    public DateTime Date { get; set; } = DateTime.UtcNow;
}
