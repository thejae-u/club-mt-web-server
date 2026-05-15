using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Models;

public enum VehicleStatus
{
    None,
    Called,
    Moving,
    Arrived
}

public enum VehicleType
{
    OwnCar,
    Taxi
}

public class Vehicle
{
    public int Id { get; set; }

    [Required]
    public int VehicleNumber { get; set; }

    [Required]
    public VehicleType Type { get; set; }

    public int? DriverId { get; set; } // FK to Participant (only for OwnCar)

    [Required]
    public VehicleStatus Status { get; set; } = VehicleStatus.None;

    public virtual ICollection<VehiclePassenger> Passengers { get; set; } = new List<VehiclePassenger>();
}
