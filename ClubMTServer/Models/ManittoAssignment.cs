using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClubMTServer.Models;

public class ManittoAssignment
{
    [Key, ForeignKey("Participant")]
    public int ParticipantId { get; set; }

    public int? TargetId { get; set; } // The ID of the person this participant is taking care of

    public int? MissionId { get; set; } // Legacy: The ID of the randomly assigned mission
    
    public bool IsMissionComplete { get; set; } = false; // Legacy: Whether the mission is marked as complete
    
    public string? MissionsJson { get; set; } // New: JSON string for multiple missions [{"MissionId": 1, "IsComplete": false}, ...]

    public virtual Participant Participant { get; set; } = null!;
}
