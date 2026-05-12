using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;
using ClubMTServer.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClubMTServer.Data;

public class ParticipantDbContext : DbContext
{
    private readonly EncryptionService _encryptionService;

    public ParticipantDbContext(DbContextOptions<ParticipantDbContext> options, EncryptionService encryptionService) : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<Participant> Participants { get; set; }
    public DbSet<ManittoMission> ManittoMissions { get; set; }
    public DbSet<ManittoReport> ManittoReports { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var converter = new ValueConverter<string?, string?>(
            v => v == null ? null : _encryptionService.Encrypt(v),
            v => v == null ? null : _encryptionService.Decrypt(v));

        modelBuilder.Entity<Participant>()
            .Property(p => p.Name)
            .HasConversion(converter!);

        modelBuilder.Entity<Participant>()
            .Property(p => p.PhoneNumber)
            .HasConversion(converter!);

        modelBuilder.Entity<Participant>()
            .Property(p => p.StudentId)
            .HasConversion(converter);

        modelBuilder.Entity<ManittoReport>()
            .HasIndex(r => r.ClientRequestId)
            .IsUnique();
    }
}
