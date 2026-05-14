using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;

using Microsoft.AspNetCore.Authorization;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly SettingsDbContext _context;

    public SettingsController(SettingsDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<SiteSettings>> GetSettings()
    {
        var settings = await _context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new SiteSettings 
            { 
                ScheduleDataJson = "[]" // Default empty schedule
            };
            _context.Settings.Add(settings);
            await _context.SaveChangesAsync();
        }
        return settings;
    }

    [HttpPut]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateSettings(SiteSettings settings)
    {
        if (settings.MaxGeneralCapacity < 0 || settings.MaxMilitaryCapacity < 0)
        {
            return BadRequest("정원은 0명 이상이어야 합니다.");
        }

        if (settings.MaxMilitaryCapacity > settings.MaxGeneralCapacity)
        {
            return BadRequest("군인 우대 정원은 일반 정원보다 클 수 없습니다.");
        }

        var existing = await _context.Settings.FirstOrDefaultAsync();
        if (existing == null)
        {
            _context.Settings.Add(settings);
        }
        else
        {
            existing.Title = settings.Title;
            existing.Subtitle = settings.Subtitle;
            existing.EventDateRange = settings.EventDateRange;
            existing.DDayTargetDate = settings.DDayTargetDate;
            existing.RegistrationDeadline = settings.RegistrationDeadline;
            existing.Location = settings.Location;
            existing.LocationDetail = settings.LocationDetail;
            existing.MapLink = settings.MapLink;
            existing.RegistrationFee = settings.RegistrationFee;
            existing.MaxGeneralCapacity = settings.MaxGeneralCapacity;
            existing.MaxMilitaryCapacity = settings.MaxMilitaryCapacity;
            existing.ScheduleDataJson = settings.ScheduleDataJson;
            existing.CommonChecklistJson = settings.CommonChecklistJson;
            existing.IsManittoPublic = settings.IsManittoPublic;
            existing.ManittoMissionCount = settings.ManittoMissionCount;
            existing.IsCookingBattlePublic = settings.IsCookingBattlePublic;
            existing.IsCookingBattleVotingActive = settings.IsCookingBattleVotingActive;
            existing.MaxCheerPerPerson = settings.MaxCheerPerPerson;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
