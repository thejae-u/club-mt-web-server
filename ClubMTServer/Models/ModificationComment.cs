using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class ModificationComment
{
    public int Id { get; set; }

    [Required]
    public int TaskId { get; set; }

    [Required]
    public string Content { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ModificationTask? Task { get; set; }
}
