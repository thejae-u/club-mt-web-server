using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public class ManittoMission
{
    public int Id { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
}
