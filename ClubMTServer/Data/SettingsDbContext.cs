using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data;

public class SettingsDbContext : DbContext
{
    public SettingsDbContext(DbContextOptions<SettingsDbContext> options) : base(options) { }

    public DbSet<SiteSettings> Settings { get; set; } = null!;
}
