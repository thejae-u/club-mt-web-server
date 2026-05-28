using System;

namespace ClubMTServer.Models
{
    public class AuditLog
    {
        public string? Username { get; set; }
        public string? Role { get; set; }
        public string? Method { get; set; }
        public string? Path { get; set; }
        public string? RequestBody { get; set; }
        public string? ResponseStatus { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }
    }
}
