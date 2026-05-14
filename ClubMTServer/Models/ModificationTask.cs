using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class ModificationTask
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }
}
