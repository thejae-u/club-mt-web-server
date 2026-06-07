using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using ClubMTServer.Services;
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
    private readonly CheerCacheService _cheerCache;

    public CookingBattleController(
        CookingBattleDbContext context, 
        ParticipantDbContext participantContext,
        SettingsDbContext settingsContext,
        CheerCacheService cheerCache)
    {
        _context = context;
        _participantContext = participantContext;
        _settingsContext = settingsContext;
        _cheerCache = cheerCache;
    }

    [HttpGet("status")]
    [Authorize]
    public async Task<ActionResult<object>> GetStatus()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var pUser = await _participantContext.Participants.FindAsync(participantId);
        if (pUser == null || !pUser.IsRegistered || pUser.IsCancelRequested) 
            return StatusCode(403, new { message = "취소 신청 상태이거나 취소된 참가자는 이용할 수 없습니다." });

        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        var isPublic = settings?.IsCookingBattlePublic ?? false;
        var isChefPublic = settings?.IsCookingBattleChefPublic ?? false;

        var myApplication = await _context.Applications
            .FirstOrDefaultAsync(a => a.ParticipantId == participantId);
        
        var myAssignment = await _context.Assignments
            .FirstOrDefaultAsync(a => a.ParticipantId == participantId);

        var teams = new List<object>();
        if (isPublic || isChefPublic)
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
                    Members = isPublic ? teamAssignments
                        .Where(a => a.Role != CookingRole.OrderChef)
                        .Select(a => {
                            var p = participants.FirstOrDefault(p => p.Id == a.ParticipantId);
                            return (object)new { 
                                Name = p?.Name ?? "Unknown", 
                                Role = a.Role.ToString() 
                            };
                        }).ToList() : new List<object>()
                });
            }
        }

        // Use cached cheer stats instead of DB count
        var stats = _cheerCache.GetGlobalStats();
        var blackCheers = stats.Black;
        var whiteCheers = stats.White;
        var totalCheers = blackCheers + whiteCheers;

        var totalParticipantsCount = await _participantContext.Participants.CountAsync(p => p.IsRegistered);
        
        // Realistic Target: Average 2.5 cheers per person, but at least 20 cheers total for stability
        double realisticTarget = Math.Max(20, (totalParticipantsCount > 0 ? totalParticipantsCount : 1) * 2.5);

        // Non-linear (Square Root) scaling: Early cheers move more, later cheers move less.
        double CalculatePct(int count) {
            if (count == 0) return 0;
            if (count >= realisticTarget) return 100.0;

            double basePct = 15.0; // 15% minimum offset for the first cheer
            
            // Scaling logic: (sqrt(count) / sqrt(target)) * 85
            // This curves the progression so it doesn't jump to 100 too fast
            double ratio = Math.Sqrt(count) / Math.Sqrt(realisticTarget);
            double progressPct = ratio * 85.0;
            
            return Math.Min(100, basePct + progressPct);
        }

        var blackPct = CalculatePct(blackCheers);
        var whitePct = CalculatePct(whiteCheers);

        var myVote = await _context.Votes.FirstOrDefaultAsync(v => v.ParticipantId == participantId);
        
        return new
        {
            IsPublic = isPublic,
            IsChefPublic = isChefPublic,
            IsVotingActive = settings?.IsCookingBattleVotingActive ?? false,
            MyApplication = myApplication,
            MyAssignment = (isPublic || isChefPublic) ? myAssignment : null,
            Teams = (isPublic || isChefPublic) ? teams : null,
            CheerStats = new {
                Black = blackCheers,
                White = whiteCheers,
                Total = totalCheers,
                BlackPercent = blackPct,
                WhitePercent = whitePct
            }
        };
    }

    public record ApplyChefRequest(string Experience, string SignatureDish);

    [HttpPost("apply")]
    [Authorize]
    [EnableRateLimiting("submit")]
    public async Task<IActionResult> Apply([FromBody] ApplyChefRequest request)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var participantId)) return Unauthorized();

        var p = await _participantContext.Participants.FindAsync(participantId);
        if (p == null) return Unauthorized();

        if (!p.IsRegistered || p.IsCancelRequested) 
            return StatusCode(403, new { message = "취소 신청 상태이거나 취소된 참가자는 이용할 수 없습니다." });

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

        var comments = await _context.Comments
            .Where(c => c.Team == cookingTeam)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync();

        var participantIds = comments.Select(c => c.ParticipantId).Distinct().ToList();
        var participants = await _participantContext.Participants
            .Where(p => participantIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();

        return comments.Select(c => {
            var p = participants.FirstOrDefault(pp => pp.Id == c.ParticipantId);
            var decryptedName = p?.Name ?? "Unknown";
            string maskedName;
            if (decryptedName.Length <= 1) maskedName = decryptedName;
            else if (decryptedName.Length == 2) maskedName = decryptedName[..1] + "*";
            else maskedName = decryptedName[..1] + new string('*', decryptedName.Length - 2) + decryptedName[^1..];

            return new { 
                c.Content, 
                c.CreatedAt, 
                c.Team,
                Author = maskedName
            };
        }).ToList();
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

        var p = await _participantContext.Participants.FindAsync(participantId);
        if (p == null || !p.IsRegistered || p.IsCancelRequested) 
            return StatusCode(403, new { message = "취소 신청 상태이거나 취소된 참가자는 이용할 수 없습니다." });

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

        var p = await _participantContext.Participants.FindAsync(participantId);
        if (p == null || !p.IsRegistered || p.IsCancelRequested) 
            return StatusCode(403, new { message = "취소 신청 상태이거나 취소된 참가자는 이용할 수 없습니다." });

        var settings = await _settingsContext.Settings.FirstOrDefaultAsync();
        var maxCheer = settings?.MaxCheerPerPerson ?? 10;

        // Use caching service for counting and adding cheers
        // This avoids immediate DB write and locking issues
        var currentCount = await _cheerCache.GetUserCheerCountAsync(participantId);
        if (currentCount >= maxCheer) return BadRequest($"인당 최대 {maxCheer}번까지만 응원할 수 있습니다.");

        if (!_cheerCache.TryAddCheer(participantId, cookingTeam, maxCheer))
        {
            return BadRequest($"인당 최대 {maxCheer}번까지만 응원할 수 있습니다.");
        }

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

        var p = await _participantContext.Participants.FindAsync(participantId);
        if (p == null || !p.IsRegistered || p.IsCancelRequested) 
            return StatusCode(403, new { message = "취소 신청 상태이거나 취소된 참가자는 이용할 수 없습니다." });

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
