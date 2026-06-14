using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data;

public class VolleyballDbContext : DbContext
{
    public VolleyballDbContext(DbContextOptions<VolleyballDbContext> options) : base(options)
    {
    }

    public DbSet<VolleyballTeam> Teams { get; set; }
    public DbSet<VolleyballMember> Members { get; set; }
    public DbSet<VolleyballMatch> Matches { get; set; }
    public DbSet<VolleyballSettings> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Define any specific constraints if needed
        modelBuilder.Entity<VolleyballTeam>().HasIndex(t => new { t.Name, t.Session }).IsUnique();
        modelBuilder.Entity<VolleyballMember>().HasIndex(m => new { m.TeamId, m.ParticipantId }).IsUnique();
    }
}
