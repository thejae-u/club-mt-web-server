using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data;

public class VehicleDbContext : DbContext
{
    public VehicleDbContext(DbContextOptions<VehicleDbContext> options) : base(options)
    {
    }

    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<VehiclePassenger> VehiclePassengers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Vehicle>()
            .HasMany(v => v.Passengers)
            .WithOne(p => p.Vehicle)
            .HasForeignKey(p => p.VehicleId);
            
        modelBuilder.Entity<VehiclePassenger>()
            .HasIndex(vp => new { vp.VehicleId, vp.ParticipantId })
            .IsUnique();
    }
}
