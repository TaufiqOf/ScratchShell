using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ScratchShell.WebApi.Models;

namespace ScratchShell.WebApi.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }
        
        public DbSet<UserSettings> UserSettings { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure User entity
            builder.Entity<User>(entity =>
            {
                entity.Property(e => e.FirstName).HasMaxLength(100);
                entity.Property(e => e.LastName).HasMaxLength(100);
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
            
            // Configure UserSettings entity
            builder.Entity<UserSettings>(entity =>
            {
                entity.HasKey(e => e.Id);
                
                entity.Property(e => e.UserId).IsRequired();
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.LastSyncedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                
                // Configure relationship
                entity.HasOne(e => e.User)
                      .WithOne(u => u.UserSettings)
                      .HasForeignKey<UserSettings>(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
                
                // Create index on UserId for faster lookups
                entity.HasIndex(e => e.UserId).IsUnique();
                
                // Create index on LastSyncedAt for cleanup operations
                entity.HasIndex(e => e.LastSyncedAt);
            });
        }
    }
}