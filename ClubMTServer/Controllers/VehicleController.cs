using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehicleController : ControllerBase
{
    private readonly VehicleDbContext _context;
    private readonly ParticipantDbContext _participantContext;
    private readonly SettingsDbContext _settingsContext;

    public VehicleController(VehicleDbContext context, ParticipantDbContext participantContext, SettingsDbContext settingsContext)
    {
        _context = context;
        _participantContext = participantContext;
        _settingsContext = settingsContext;
    }

    [HttpGet("all")]
    [Authorize]
    public async Task<ActionResult<object>> GetAllVehicles()
    {
        var settings = await _settingsContext.Settings.FirstOrDefaultAsync() ?? new SiteSettings();
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");

        if (!settings.IsVehicleAssignmentPublic && !isAdmin)
        {
            return Ok(new { isPublic = false, vehicles = new List<object>() });
        }

        var vehicles = await _context.Vehicles
            .Include(v => v.Passengers)
            .OrderBy(v => v.VehicleNumber)
            .ToListAsync();

        var participants = await _participantContext.Participants.ToListAsync();
        var participantMap = participants.ToDictionary(p => p.Id, p => p);

        var result = vehicles.Select(v => new
        {
            v.Id,
            v.VehicleNumber,
            Type = v.Type.ToString(),
            Status = v.Status.ToString(),
            Driver = v.DriverId.HasValue && participantMap.ContainsKey(v.DriverId.Value) 
                ? new { Id = v.DriverId.Value, Name = participantMap[v.DriverId.Value].Name, Generation = participantMap[v.DriverId.Value].Generation }
                : null,
            Passengers = v.Passengers.OrderBy(p => p.Order).Select(p => 
            {
                if (participantMap.ContainsKey(p.ParticipantId))
                {
                    var part = participantMap[p.ParticipantId];
                    return new { Id = part.Id, Name = part.Name, Generation = part.Generation };
                }
                return null;
            }).Where(p => p != null).ToList()
        });

        return Ok(new { isPublic = true, vehicles = result });
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<object>> GetMyVehicle()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var settings = await _settingsContext.Settings.FirstOrDefaultAsync() ?? new SiteSettings();
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");

        if (!settings.IsVehicleAssignmentPublic && !isAdmin)
        {
            return Ok(new { isPublic = false });
        }

        var passengerRecord = await _context.VehiclePassengers
            .Include(vp => vp.Vehicle)
            .ThenInclude(v => v.Passengers)
            .FirstOrDefaultAsync(vp => vp.ParticipantId == participantId);

        if (passengerRecord == null) return NotFound("배정된 차량이 없습니다.");

        var vehicle = passengerRecord.Vehicle;
        var participants = await _participantContext.Participants.ToListAsync();
        var participantMap = participants.ToDictionary(p => p.Id, p => p);

        return Ok(new
        {
            isPublic = true,
            vehicle = new
            {
                vehicle.Id,
                vehicle.VehicleNumber,
                Type = vehicle.Type.ToString(),
                Status = vehicle.Status.ToString(),
                Driver = vehicle.DriverId.HasValue && participantMap.ContainsKey(vehicle.DriverId.Value)
                    ? new { Id = vehicle.DriverId.Value, Name = participantMap[vehicle.DriverId.Value].Name, Generation = participantMap[vehicle.DriverId.Value].Generation }
                    : null,
                Passengers = vehicle.Passengers.OrderBy(p => p.Order).Select(p =>
                {
                    if (participantMap.ContainsKey(p.ParticipantId))
                    {
                        var part = participantMap[p.ParticipantId];
                        return new { Id = part.Id, Name = part.Name, Generation = part.Generation };
                    }
                    return null;
                }).Where(p => p != null).ToList()
            }
        });
    }

    [HttpPost("status")]
    [Authorize]
    public async Task<IActionResult> UpdateStatus([FromBody] VehicleStatusUpdateDto update)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var vehicle = await _context.Vehicles.FindAsync(update.VehicleId);
        if (vehicle == null) return NotFound();

        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");

        // Only driver or admin can update status
        if (!isAdmin && vehicle.DriverId != participantId)
        {
            return Forbid("차장만 상태를 변경할 수 있습니다.");
        }

        vehicle.Status = update.Status;
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("admin/generate")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GenerateAssignments([FromBody] VehicleGenerationRequestDto request)
    {
        // 1. Clear existing
        _context.VehiclePassengers.RemoveRange(_context.VehiclePassengers);
        _context.Vehicles.RemoveRange(_context.Vehicles);
        await _context.SaveChangesAsync();

        // 2. Get all participants who are registered and not waitlisted
        var allParticipants = await _participantContext.Participants
            .Where(p => p.IsRegistered && !p.IsWaitlisted)
            .ToListAsync();

        var assignedParticipantIds = new HashSet<int>();
        var vehicles = new List<(Vehicle Vehicle, int Capacity)>();

        // 3. Create Vehicles based on request
        int vehicleNum = 1;
        foreach (var vReq in request.Vehicles)
        {
            var vehicle = new Vehicle
            {
                VehicleNumber = vehicleNum++,
                Type = vReq.DriverId.HasValue ? VehicleType.OwnCar : VehicleType.Taxi,
                DriverId = vReq.DriverId,
                Status = VehicleStatus.None
            };
            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync(); // To get vehicle ID

            vehicles.Add((vehicle, vReq.Capacity));

            if (vReq.DriverId.HasValue)
            {
                _context.VehiclePassengers.Add(new VehiclePassenger
                {
                    VehicleId = vehicle.Id,
                    ParticipantId = vReq.DriverId.Value,
                    Order = 0
                });
                assignedParticipantIds.Add(vReq.DriverId.Value);
            }
        }

        // 4. Randomly assign remaining participants with even distribution (Round-Robin)
        var remainingParticipants = allParticipants
            .Where(p => !assignedParticipantIds.Contains(p.Id))
            .OrderBy(x => Guid.NewGuid()) // Random shuffle
            .ToList();

        var vehicleCurrentCounts = vehicles.ToDictionary(
            v => v.Vehicle.Id, 
            v => v.Vehicle.DriverId.HasValue ? 1 : 0
        );

        int pIndex = 0;
        bool addedAny;
        do
        {
            addedAny = false;
            foreach (var vData in vehicles)
            {
                if (pIndex >= remainingParticipants.Count) break;

                var vehicle = vData.Vehicle;
                int capacity = vData.Capacity;
                
                if (vehicleCurrentCounts[vehicle.Id] < capacity)
                {
                    var p = remainingParticipants[pIndex++];
                    _context.VehiclePassengers.Add(new VehiclePassenger
                    {
                        VehicleId = vehicle.Id,
                        ParticipantId = p.Id,
                        Order = vehicleCurrentCounts[vehicle.Id]++
                    });
                    assignedParticipantIds.Add(p.Id);
                    addedAny = true;
                }
            }
        } while (addedAny && pIndex < remainingParticipants.Count);

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("admin/update")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateAssignments([FromBody] List<VehicleUpdateDto> updates)
    {
        // Simplified update: clear and recreate based on provided data
        _context.VehiclePassengers.RemoveRange(_context.VehiclePassengers);
        _context.Vehicles.RemoveRange(_context.Vehicles);
        await _context.SaveChangesAsync();

        foreach (var vUpdate in updates)
        {
            var vehicle = new Vehicle
            {
                VehicleNumber = vUpdate.VehicleNumber,
                Type = vUpdate.Type,
                DriverId = vUpdate.DriverId,
                Status = vUpdate.Status
            };
            _context.Vehicles.Add(vehicle);
            await _context.SaveChangesAsync();

            int order = 0;
            foreach (var pId in vUpdate.PassengerIds)
            {
                // Ensure the driver is always at the top of the list if they are in passenger list
                // (Though usually driver is handled separately or included in passenger list)
                _context.VehiclePassengers.Add(new VehiclePassenger
                {
                    VehicleId = vehicle.Id,
                    ParticipantId = pId,
                    Order = order++
                });
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("admin/reset")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetAssignments()
    {
        _context.VehiclePassengers.RemoveRange(_context.VehiclePassengers);
        _context.Vehicles.RemoveRange(_context.Vehicles);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("admin/toggle-public")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> TogglePublic()
    {
        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        if (settings == null) return NotFound();

        settings.IsVehicleAssignmentPublic = !settings.IsVehicleAssignmentPublic;
        await _settingsContext.SaveChangesAsync();

        return Ok(new { isPublic = settings.IsVehicleAssignmentPublic });
    }
}

public class VehicleStatusUpdateDto
{
    public int VehicleId { get; set; }
    public VehicleStatus Status { get; set; }
}

public class VehicleGenerationRequestDto
{
    public List<VehicleRequestDto> Vehicles { get; set; } = new();
}

public class VehicleRequestDto
{
    public int? DriverId { get; set; }
    public int Capacity { get; set; }
}

public class VehicleUpdateDto
{
    public int VehicleNumber { get; set; }
    public VehicleType Type { get; set; }
    public int? DriverId { get; set; }
    public VehicleStatus Status { get; set; }
    public List<int> PassengerIds { get; set; } = new();
}
