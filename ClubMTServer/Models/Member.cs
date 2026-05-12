using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class Member
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string NameHash { get; set; } = string.Empty; // Blind Index for search

    [Required]
    public int Generation { get; set; }

    [Required]
    public ParticipantType Type { get; set; } = ParticipantType.Student;

    public string? PhoneNumber { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
