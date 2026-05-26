using ClubMTServer.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubMTServer.Data
{
    public class PhotoDbContext : DbContext
    {
        public PhotoDbContext(DbContextOptions<PhotoDbContext> options) : base(options) { }

        public DbSet<PhotoSession> PhotoSessions { get; set; } = null!;
        public DbSet<Photo> Photos { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PhotoSession>()
                .HasMany(s => s.Photos)
                .WithOne()
                .HasForeignKey(p => p.PhotoSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
