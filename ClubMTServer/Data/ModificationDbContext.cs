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
    public DbSet<ModificationComment> ModificationComments { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModificationTask>().ToTable("ModificationTasks");
        modelBuilder.Entity<ModificationComment>().ToTable("ModificationComments");

        modelBuilder.Entity<ModificationComment>()
            .HasOne(c => c.Task)
            .WithMany(t => t.Comments)
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
