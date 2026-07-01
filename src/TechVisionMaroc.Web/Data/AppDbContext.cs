using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Models;

namespace TechVisionMaroc.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Produit> Produits => Set<Produit>();
    public DbSet<Categorie> Categories => Set<Categorie>();
    public DbSet<Utilisateur> Utilisateurs => Set<Utilisateur>();
    public DbSet<Commande> Commandes => Set<Commande>();
    public DbSet<LigneCommande> LignesCommande => Set<LigneCommande>();
    public DbSet<Panier> Paniers => Set<Panier>();
    public DbSet<LignePanier> LignesPanier => Set<LignePanier>();
    public DbSet<Avis> Avis => Set<Avis>();
    public DbSet<RecommandationProduit> RecommandationsProduits => Set<RecommandationProduit>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<TransactionFidelite> TransactionsFidelite => Set<TransactionFidelite>();
    public DbSet<EvaluationFraude> EvaluationsFraude => Set<EvaluationFraude>();
    public DbSet<EvenementComportement> EvenementsComportement => Set<EvenementComportement>();
    public DbSet<ScoreRFM> ScoresRFM => Set<ScoreRFM>();
    public DbSet<AbonneNewsletter> AbonnesNewsletter => Set<AbonneNewsletter>();
    public DbSet<AdresseClient> AdressesClient => Set<AdresseClient>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<UtilisationCoupon> UtilisationsCoupon => Set<UtilisationCoupon>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Abonnement> Abonnements => Set<Abonnement>();
    public DbSet<Devis> Devis => Set<Devis>();
    public DbSet<LigneDevis> LignesDevis => Set<LigneDevis>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Produit>().HasIndex(p => p.Reference).IsUnique();
        builder.Entity<Produit>().HasIndex(p => p.Nom);

        builder.Entity<Utilisateur>().HasIndex(u => u.Email).IsUnique();

        builder.Entity<Commande>().HasIndex(c => c.NumeroCommande).IsUnique();

        builder.Entity<Wishlist>(e =>
        {
            e.HasIndex(w => new { w.UtilisateurId, w.ProduitId }).IsUnique();
            e.HasOne(w => w.Utilisateur).WithMany().HasForeignKey(w => w.UtilisateurId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.Produit).WithMany().HasForeignKey(w => w.ProduitId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<RecommandationProduit>(e =>
        {
            e.HasIndex(r => new { r.ProduitId, r.ProduitRecommandeId }).IsUnique();
            e.HasIndex(r => r.ProduitId);
            e.HasOne(r => r.Produit).WithMany().HasForeignKey(r => r.ProduitId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.ProduitRecommande).WithMany().HasForeignKey(r => r.ProduitRecommandeId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<Categorie>().HasData(
            new Categorie { Id = 1, Nom = "Ordinateurs & Laptops", Slug = "ordinateurs", IconeClass = "fa-laptop" },
            new Categorie { Id = 2, Nom = "Imprimantes & Scanners", Slug = "imprimantes", IconeClass = "fa-print" },
            new Categorie { Id = 3, Nom = "Réseaux & Connectivité", Slug = "reseaux", IconeClass = "fa-network-wired" },
            new Categorie { Id = 4, Nom = "Fournitures de Bureau", Slug = "fournitures", IconeClass = "fa-briefcase" },
            new Categorie { Id = 5, Nom = "Accessoires Informatiques", Slug = "accessoires", IconeClass = "fa-keyboard" },
            new Categorie { Id = 6, Nom = "Stockage & Sauvegarde", Slug = "stockage", IconeClass = "fa-hard-drive" }
        );
    }
}
