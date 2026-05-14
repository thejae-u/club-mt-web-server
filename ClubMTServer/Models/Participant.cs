using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public enum ParticipantType
{
    Student,  // 재학생 (0)
    Alumni,   // 졸업생 (1)
    Leave,    // 휴학생 (2)
    Military, // 군인   (3)
    Other     // 기타   (4)
}

public class Participant
{
    public int Id { get; set; }

    [Required]
    public ParticipantType Type { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string NameHash { get; set; } = string.Empty; // Blind Index for search

    [Required]
    public int Generation { get; set; }

    public string? StudentId { get; set; }

    [Phone]
    public string? PhoneNumber { get; set; }

    [Required]
    public string Password { get; set; } = string.Empty; // Hashed password

    public int ParticipationCount { get; set; }

    public string? MemoryOrExpectation { get; set; }

    public string ParticipationSchedule { get; set; } = "Full"; // Full or Partial

    public string? MidJoinDetails { get; set; }

    public string Transportation { get; set; } = "Walk"; // Car or Walk

    public bool IsCarpoolAvailable { get; set; } = false;

    public int CarpoolSeats { get; set; } = 0;

    public string? DepartureArea { get; set; }

    public string? Allergies { get; set; }

    public string? Remarks { get; set; }

    public string? OneLineExpectation { get; set; }

    public bool HasDriverLicense { get; set; } = false;

    public string? DriverLicenseType { get; set; } // 1종 보통, 2종 보통, 등

    public bool CanDrive { get; set; } = false; // 실제 운전 가능 여부

    public string? DrivingExperience { get; set; } // 운전 경력

    public bool IsMilitaryPriority { get; set; }

    public string? MilitaryStatus { get; set; } // Confirmed, Pending, etc.

    public bool IsRegistered { get; set; } = false;

    public bool IsWaitlisted { get; set; } = false;

    public bool IsDepositConfirmed { get; set; } = false;

    public string? ChecklistJson { get; set; } // JSON string for personal tasks

    public string? CommonChecklistStatusJson { get; set; } // JSON string for personal status of common tasks

    // Manitto Relation
    public virtual ManittoAssignment? ManittoAssignment { get; set; }

    public string? MbtiResult { get; set; } // EPHA, ITSL, ETHL, ITSA

    public string? CurrentSessionId { get; set; } // For single-session management

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
