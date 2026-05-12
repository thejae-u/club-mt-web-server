using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data;

public class FeeDbContext : DbContext
{
    public FeeDbContext(DbContextOptions<FeeDbContext> options) : base(options) { }

    public DbSet<FeeHistory> FeeHistories { get; set; } = null!;
}
