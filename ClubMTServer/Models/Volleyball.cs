using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClubMTServer.Models;

public enum VolleyballSession
{
    FirstHalf = 1,  // 1부 (당일 추가 팀원)
    SecondHalf = 2  // 2부 (미리 짜여진 팀원)
}

public class VolleyballTeam
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty; // A, B, C, D
    
    public VolleyballSession Session { get; set; } // 1부 or 2부
    
    public string? Color { get; set; }
    
    public int Wins { get; set; } = 0;
    public int Losses { get; set; } = 0;
    
    public int MiniGamePoints { get; set; } = 0; // 타겟 맞추기 등 점수 합산
    
    public int Points { get; set; } = 0; // Total points (Wins + MiniGamePoints)
}

public class VolleyballMember
{
    public int Id { get; set; }
    
    public int TeamId { get; set; }
    
    public int ParticipantId { get; set; }
    
    public string? ParticipantName { get; set; }
}

public enum MatchStatus
{
    Pending,
    InProgress,
    Completed
}

public class VolleyballMatch
{
    public int Id { get; set; }
    
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    
    public int? WinnerTeamId { get; set; }
    
    public MatchStatus Status { get; set; } = MatchStatus.Pending;
    
    public int MatchOrder { get; set; }
    
    public VolleyballSession Session { get; set; } // 1부 or 2부 경기 구분
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class VolleyballSettings
{
    public int Id { get; set; }
    public bool IsFirstHalfPublic { get; set; } = false;
    public bool IsSecondHalfPublic { get; set; } = false;
}
