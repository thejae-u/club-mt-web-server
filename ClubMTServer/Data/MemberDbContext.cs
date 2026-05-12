using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;
using ClubMTServer.Services;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ClubMTServer.Data;

public class MemberDbContext : DbContext
{
    private readonly EncryptionService _encryptionService;

    public MemberDbContext(DbContextOptions<MemberDbContext> options, EncryptionService encryptionService) : base(options)
    {
        _encryptionService = encryptionService;
    }

    public DbSet<Member> Members { get; set; } = null!;
    public DbSet<AdminUser> AdminUsers { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var converter = new ValueConverter<string?, string?>(
            v => v == null ? null : _encryptionService.Encrypt(v),
            v => v == null ? null : _encryptionService.Decrypt(v));

        modelBuilder.Entity<Member>()
            .Property(m => m.Name)
            .HasConversion(converter!);

        modelBuilder.Entity<Member>()
            .Property(m => m.PhoneNumber)
            .HasConversion(converter!);
    }
}
