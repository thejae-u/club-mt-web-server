using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class SiteSettings
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; } = "제 0회 빅이큐 총동문 MT";

    public string Subtitle { get; set; } = "";

    [Required]
    public string EventDateRange { get; set; } = "2026년 0월 00일 ~ 00일";

    public DateTime? DDayTargetDate { get; set; } 

    public DateTime? RegistrationDeadline { get; set; }

    [Required]
    public string Location { get; set; } = "OO 리조트";

    public string LocationDetail { get; set; } = "";

    public string MapLink { get; set; } = "";

    public int RegistrationFee { get; set; } = 50000;

    public int MaxGeneralCapacity { get; set; } = 16;

    public int MaxMilitaryCapacity { get; set; } = 4;

    public string ScheduleDataJson { get; set; } = "[]";

    public string CommonChecklistJson { get; set; } = "[]";

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
