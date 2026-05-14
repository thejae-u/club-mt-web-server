using ClubMTServer.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubMTServer.Data;

public class ModificationDbContext : DbContext
{
    public ModificationDbContext(DbContextOptions<ModificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<ModificationTask> ModificationTasks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModificationTask>().ToTable("ModificationTasks");
    }
}
