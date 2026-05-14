using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public enum CookingTeam
{
    None,
    Black,
    White
}

public enum CookingRole
{
    Spectator,
    OrderChef,
    Avatar,
    Assistant
}

public class CookingBattleApplication
{
    public int Id { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public int Generation { get; set; }

    public string? Experience { get; set; } // 경력 혹은 포부

    public string? SignatureDish { get; set; } // 자신있는 요리

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CookingBattleAssignment
{
    public int Id { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public CookingTeam Team { get; set; }

    [Required]
    public CookingRole Role { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CookingBattleComment
{
    public int Id { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public CookingTeam Team { get; set; }

    [Required]
    [MaxLength(200)]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CookingBattleCheer
{
    public int Id { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public CookingTeam Team { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class CookingBattleVote
{
    public int Id { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public CookingTeam Team { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
