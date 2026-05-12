using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class ManittoReport
{
    public int Id { get; set; }

    [Required]
    [MaxLength(1000)]
    public string Content { get; set; } = string.Empty;

    public int? ParticipantId { get; set; }
    public string? ParticipantName { get; set; }
    public int? ParticipantGeneration { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string? ClientRequestId { get; set; }
}
