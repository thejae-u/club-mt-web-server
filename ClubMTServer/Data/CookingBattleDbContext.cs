using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data;

public class CookingBattleDbContext : DbContext
{
    public CookingBattleDbContext(DbContextOptions<CookingBattleDbContext> options) : base(options)
    {
    }

    public DbSet<CookingBattleApplication> Applications { get; set; }
    public DbSet<CookingBattleAssignment> Assignments { get; set; }
    public DbSet<CookingBattleComment> Comments { get; set; }
    public DbSet<CookingBattleCheer> Cheers { get; set; }
    public DbSet<CookingBattleVote> Votes { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CookingBattleAssignment>()
            .HasIndex(a => a.ParticipantId)
            .IsUnique();

        modelBuilder.Entity<CookingBattleVote>()
            .HasIndex(v => v.ParticipantId)
            .IsUnique();
    }
}
