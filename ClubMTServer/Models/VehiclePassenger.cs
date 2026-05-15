using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class VehiclePassenger
{
    public int Id { get; set; }

    [Required]
    public int VehicleId { get; set; }

    [Required]
    public int ParticipantId { get; set; }

    [Required]
    public int Order { get; set; } // To maintain ordering in the list

    public virtual Vehicle Vehicle { get; set; } = null!;
}
