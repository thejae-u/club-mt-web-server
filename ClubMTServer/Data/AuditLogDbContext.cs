using Microsoft.EntityFrameworkCore;
using ClubMTServer.Models;

namespace ClubMTServer.Data
{
    public class AuditLogDbContext : DbContext
    {
        public AuditLogDbContext(DbContextOptions<AuditLogDbContext> options) : base(options) { }
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    }
}
