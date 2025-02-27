using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AirbnbShopApi.Models;

namespace AirbnbShopApi.Data
{
    public class AppDbContext : IdentityDbContext<ApplicationUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Apartment> Apartments { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<UserApartment> UserApartments { get; set; } = null!;
        public DbSet<AdminTransaction> AdminTransactions { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<RetentionRate> RetentionRates { get; set; } = null!;
        public DbSet<ReconciliationProtocol> ReconciliationProtocols { get; set; }
        public DbSet<ProtocolComment> ProtocolComments { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Apartment>()
                .HasMany(a => a.Products)
                .WithOne(p => p.Apartment)
                .HasForeignKey(p => p.ApartmentId);

            modelBuilder.Entity<Apartment>()
                .HasMany(a => a.AdminTransactions)
                .WithOne(at => at.Apartment)
                .HasForeignKey(at => at.ApartmentId);

            modelBuilder.Entity<UserApartment>()
                .HasKey(ua => new { ua.UserId, ua.ApartmentId });

            modelBuilder.Entity<UserApartment>()
                .HasOne(ua => ua.User)
                .WithMany(u => u.UserApartments)
                .HasForeignKey(ua => ua.UserId);

            modelBuilder.Entity<UserApartment>()
                .HasOne(ua => ua.Apartment)
                .WithMany(a => a.UserApartments)
                .HasForeignKey(ua => ua.ApartmentId);

            // Configuração corrigida: um Product pode ter várias Transactions
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Transactions)
                .WithOne(t => t.Product)
                .HasForeignKey(t => t.ProductId)
                .OnDelete(DeleteBehavior.Cascade); // Opcional: remove transações se o produto for excluído
            
            modelBuilder.Entity<Product>()
                .Property(p => p.Image)
                .HasColumnType("LONGBLOB"); // Explicitamente define como LONGBLOB
            
        }
        
    }
}