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
public class CookingBattleController : ControllerBase
{
    private readonly CookingBattleDbContext _context;
    private readonly ParticipantDbContext _participantContext;
    private readonly SettingsDbContext _settingsContext;

    public CookingBattleController(
        CookingBattleDbContext context, 
        ParticipantDbContext participantContext,
        SettingsDbContext settingsContext)
    {
        _context = context;
        _participantContext = participantContext;
        _settingsContext = settingsContext;
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<object>> GetStatus()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        var isPublic = settings?.IsCookingBattlePublic ?? false;

        var myApplication = await _context.Applications
            .FirstOrDefaultAsync(a => a.ParticipantId == participantId);
        
        var myAssignment = await _context.Assignments
            .FirstOrDefaultAsync(a => a.ParticipantId == participantId);

        var teams = new List<object>();
        if (isPublic)
        {
            var assignments = await _context.Assignments.ToListAsync();
            var participants = await _participantContext.Participants
                .Select(p => new { p.Id, p.Name, p.Generation })
                .ToListAsync();

            var apps = await _context.Applications.ToListAsync();

            foreach (var team in new[] { CookingTeam.Black, CookingTeam.White })
            {
                var teamAssignments = assignments.Where(a => a.Team == team).ToList();
                var chefAssignment = teamAssignments.FirstOrDefault(a => a.Role == CookingRole.OrderChef);
                var chefInfo = chefAssignment != null ? participants.FirstOrDefault(p => p.Id == chefAssignment.ParticipantId) : null;
                var chefApp = chefAssignment != null ? apps.FirstOrDefault(a => a.ParticipantId == chefAssignment.ParticipantId) : null;

                teams.Add(new
                {
                    Team = team.ToString(),
                    Chef = chefInfo != null ? new { 
                        chefInfo.Name, 
                        chefInfo.Generation,
                        chefApp?.Experience,
                        chefApp?.SignatureDish
                    } : null,
                    Members = teamAssignments
                        .Where(a => a.Role != CookingRole.OrderChef)
                        .Select(a => {
                            var p = participants.FirstOrDefault(p => p.Id == a.ParticipantId);
                            return new { 
                                Name = p?.Name ?? "Unknown", 
                                Role = a.Role.ToString() 
                            };
                        })
                });
            }
        }

        var blackCheers = await _context.Cheers.CountAsync(c => c.Team == CookingTeam.Black);
        var whiteCheers = await _context.Cheers.CountAsync(c => c.Team == CookingTeam.White);
        var totalCheers = blackCheers + whiteCheers;

        var maxCheer = settings?.MaxCheerPerPerson ?? 10;
        var spectatorsCount = await _context.Assignments.CountAsync(a => a.Role == CookingRole.Spectator);
        double maxPossibleCheers = (spectatorsCount > 0 ? spectatorsCount : 1) * maxCheer;

        double blackPct = Math.Min(100, (blackCheers / maxPossibleCheers) * 100);
        double whitePct = Math.Min(100, (whiteCheers / maxPossibleCheers) * 100);

        return new
        {
            IsPublic = isPublic,
            IsVotingActive = settings?.IsCookingBattleVotingActive ?? false,
            MyApplication = myApplication,
            MyAssignment = isPublic ? myAssignment : null,
            Teams = isPublic ? teams : null,
            CheerStats = new {
                Black = blackCheers,
                White = whiteCheers,
                Total = totalCheers,
                BlackPercent = blackPct,
                WhitePercent = whitePct
            }
        };
    }

    [HttpPost("apply")]
    [Authorize]
    [EnableRateLimiting("submit")]
    public async Task<IActionResult> Apply([FromBody] CookingBattleApplication request)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var p = await _participantContext.Participants.FindAsync(participantId);
        if (p == null) return Unauthorized();

        var existing = await _context.Applications.FirstOrDefaultAsync(a => a.ParticipantId == participantId);
        if (existing != null) return BadRequest("이미 신청하셨습니다.");

        var app = new CookingBattleApplication
        {
            ParticipantId = p.Id,
            Name = p.Name,
            Generation = p.Generation,
            Experience = request.Experience,
            SignatureDish = request.SignatureDish
        };

        _context.Applications.Add(app);
        await _context.SaveChangesAsync();

        return Ok(new { message = "셰프 지원이 완료되었습니다." });
    }

    [HttpGet("comments/{team}")]
    public async Task<ActionResult<IEnumerable<object>>> GetComments(string team)
    {
        if (!Enum.TryParse<CookingTeam>(team, true, out var cookingTeam) || cookingTeam == CookingTeam.None)
            return BadRequest("잘못된 팀 선택입니다.");

        return await _context.Comments
            .Where(c => c.Team == cookingTeam)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new { c.Content, c.CreatedAt, c.Team })
            .ToListAsync();
    }

    [HttpPost("comments/{team}")]
    [Authorize]
    [EnableRateLimiting("submit")]
    public async Task<IActionResult> PostComment(string team, [FromBody] CookingBattleComment request, [FromServices] SettingsDbContext settingsContext)
    {
        var settings = await settingsContext.Settings.FirstOrDefaultAsync();
        if (settings == null || !settings.IsCookingBattlePublic)
            return BadRequest("아직 팀이 공개되지 않아 한줄평을 남길 수 없습니다.");

        if (!await _context.Assignments.AnyAsync())
            return BadRequest("아직 팀 배정이 완료되지 않아 한줄평을 남길 수 없습니다.");

        if (!Enum.TryParse<CookingTeam>(team, true, out var cookingTeam) || cookingTeam == CookingTeam.None)
            return BadRequest("잘못된 팀 선택입니다.");

        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest("내용을 입력해주세요.");
        if (request.Content.Length > 200) return BadRequest("200자 이내로 작성해주세요.");

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var comment = new CookingBattleComment
        {
            ParticipantId = participantId,
            Team = cookingTeam,
            Content = request.Content
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync();

        return Ok(new { message = "한줄평이 등록되었습니다." });
    }

    [HttpPost("cheer/{team}")]
    [Authorize]
    [EnableRateLimiting("cheer")]
    public async Task<IActionResult> Cheer(string team)
    {
        if (!Enum.TryParse<CookingTeam>(team, true, out var cookingTeam) || cookingTeam == CookingTeam.None)
            return BadRequest("잘못된 팀 선택입니다.");

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        var maxCheer = settings?.MaxCheerPerPerson ?? 10;

        var myCheerCount = await _context.Cheers.CountAsync(c => c.ParticipantId == participantId);
        if (myCheerCount >= maxCheer) return BadRequest($"인당 최대 {maxCheer}번까지만 응원할 수 있습니다.");

        var cheer = new CookingBattleCheer
        {
            ParticipantId = participantId,
            Team = cookingTeam
        };

        _context.Cheers.Add(cheer);
        await _context.SaveChangesAsync();

        return Ok(new { message = "응원 감사합니다!" });
    }

    [HttpPost("vote/{team}")]
    [Authorize]
    public async Task<IActionResult> Vote(string team)
    {
        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        if (settings == null || !settings.IsCookingBattleVotingActive)
            return BadRequest("지금은 투표 기간이 아닙니다.");

        if (!Enum.TryParse<CookingTeam>(team, true, out var cookingTeam) || cookingTeam == CookingTeam.None)
            return BadRequest("잘못된 팀 선택입니다.");

        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var existing = await _context.Votes.FirstOrDefaultAsync(v => v.ParticipantId == participantId);
        if (existing != null) return BadRequest("이미 투표하셨습니다.");

        var vote = new CookingBattleVote
        {
            ParticipantId = participantId,
            Team = cookingTeam
        };

        _context.Votes.Add(vote);
        await _context.SaveChangesAsync();

        return Ok(new { message = "투표가 완료되었습니다." });
    }
}
