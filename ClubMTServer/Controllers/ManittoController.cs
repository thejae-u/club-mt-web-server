using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ManittoController : ControllerBase
{
    private readonly ParticipantDbContext _context;
    private readonly ClubMTServer.Services.EncryptionService _encryption;

    public ManittoController(ParticipantDbContext context, ClubMTServer.Services.EncryptionService encryption)
    {
        _context = context;
        _encryption = encryption;
    }

    [HttpGet("reports")]
    public async Task<ActionResult<IEnumerable<ManittoReport>>> GetReports()
    {
        return await _context.ManittoReports
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync();
    }

    [HttpPost("reports")]
    [Authorize]
    [EnableRateLimiting("submit")]
    public async Task<IActionResult> PostReport([FromBody] string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return BadRequest("내용을 입력해주세요.");
        if (content.Length > 1000) return BadRequest("제보 내용은 1000자 이내로 작성해주세요.");

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return Unauthorized();

        var report = new ManittoReport 
        { 
            Content = content,
            ParticipantId = p.Id,
            ParticipantName = p.Name,
            ParticipantGeneration = p.Generation
        };
        _context.ManittoReports.Add(report);
        await _context.SaveChangesAsync();

        return Ok(new { message = "제보가 접수되었습니다." });
    }

    [HttpDelete("reports/{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteReport(int id)
    {
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        if (!isAdmin) return Unauthorized();

        var report = await _context.ManittoReports.FindAsync(id);
        if (report == null) return NotFound();

        _context.ManittoReports.Remove(report);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<object>> GetMyManitto()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        if (p.ManittoTargetId == null) return Ok(new { message = "아직 마니또가 배정되지 않았습니다." });

        var target = await _context.Participants.FindAsync(p.ManittoTargetId);
        var mission = await _context.ManittoMissions.FindAsync(p.ManittoMissionId);

        return new
        {
            TargetName = target != null ? target.Name : "Unknown",
            TargetGeneration = target?.Generation ?? 0,
            MissionDescription = mission?.Description ?? "미션이 없습니다.",
            IsComplete = p.IsManittoMissionComplete
        };
    }

    [HttpPost("me/complete-mission")]
    [Authorize]
    public async Task<IActionResult> CompleteMission()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        if (p.ManittoMissionId == null) return BadRequest("배정된 미션이 없습니다.");

        p.IsManittoMissionComplete = true;
        await _context.SaveChangesAsync();

        return Ok(new { message = "미션 완료 처리가 되었습니다!" });
    }
}
