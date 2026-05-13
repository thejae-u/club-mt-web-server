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
    public async Task<IActionResult> PostReport([FromBody] ManittoReportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("내용을 입력해주세요.");
        if (request.Content.Length > 1000) return BadRequest("제보 내용은 1000자 이내로 작성해주세요.");

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. 최근 제보 시간 체크 (5초 간격 제한)
            var throttleThreshold = DateTime.UtcNow.AddSeconds(-5);
            var lastReport = await _context.ManittoReports
                .Where(r => r.ParticipantId == id)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (lastReport != null && lastReport.CreatedAt > throttleThreshold)
            {
                return BadRequest("제보는 5초 간격으로 가능합니다. 잠시 후 다시 시도해주세요.");
            }

            // 2. ClientRequestId 중복 체크
            if (!string.IsNullOrEmpty(request.ClientRequestId))
            {
                var exists = await _context.ManittoReports.AnyAsync(r => r.ClientRequestId == request.ClientRequestId);
                if (exists) return Ok(new { message = "이미 처리된 요청입니다." });
            }

            // 3. 최근 10초 내 동일 내용 중복 체크
            var recentThreshold = DateTime.UtcNow.AddSeconds(-10);
            var isDuplicate = await _context.ManittoReports
                .AnyAsync(r => r.ParticipantId == id && r.Content == request.Content && r.CreatedAt > recentThreshold);

            if (isDuplicate) return Ok(new { message = "이미 동일한 제보가 접수되었습니다. 잠시 후 다시 시도해주세요." });

            var p = await _context.Participants.FindAsync(id);
            if (p == null) return Unauthorized();

            var report = new ManittoReport 
            { 
                Content = request.Content,
                ParticipantId = p.Id,
                ParticipantName = p.Name,
                ParticipantGeneration = p.Generation,
                ClientRequestId = request.ClientRequestId,
                CreatedAt = DateTime.UtcNow
            };
            _context.ManittoReports.Add(report);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "제보가 접수되었습니다." });
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync();
            return Ok(new { message = "이미 처리된 요청입니다." });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, "처리 중 오류가 발생했습니다.");
        }
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
