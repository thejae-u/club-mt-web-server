using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VolleyballController : ControllerBase
{
    private readonly VolleyballDbContext _context;
    private readonly ParticipantDbContext _participantContext;

    public VolleyballController(VolleyballDbContext context, ParticipantDbContext participantContext)
    {
        _context = context;
        _participantContext = participantContext;
    }

    [HttpGet("dashboard")]
    [Authorize]
    public async Task<ActionResult<object>> GetDashboard()
    {
        var teams = await _context.Teams.OrderBy(t => t.Id).ToListAsync();
        var members = await _context.Members.ToListAsync();
        var matches = await _context.Matches.OrderBy(m => m.MatchOrder).ToListAsync();
        var settings = await _context.Settings.FirstOrDefaultAsync() ?? new VolleyballSettings();
        
        return Ok(new { teams, members, matches, settings });
    }

    // 1. Manual Team Management
    [HttpPost("teams")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateTeam([FromBody] VolleyballSession session)
    {
        var existingTeams = await _context.Teams.Where(t => t.Session == session).ToListAsync();
        if (existingTeams.Count >= 4) return BadRequest("최대 4팀까지만 생성 가능합니다.");

        // Find the first available name from A, B, C, D
        string teamName = "A";
        for (int i = 0; i < 4; i++)
        {
            var candidate = ((char)('A' + i)).ToString();
            if (!existingTeams.Any(t => t.Name == candidate))
            {
                teamName = candidate;
                break;
            }
        }

        var team = new VolleyballTeam { Name = teamName, Session = session };
        _context.Teams.Add(team);
        await _context.SaveChangesAsync();
        return Ok(team);
    }

    [HttpDelete("teams/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteTeam(int id)
    {
        var team = await _context.Teams.FindAsync(id);
        if (team == null) return NotFound();

        // Remove members of this team
        var members = _context.Members.Where(m => m.TeamId == id);
        _context.Members.RemoveRange(members);

        _context.Teams.Remove(team);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // 2. Member Management
    [HttpPost("teams/{teamId}/members")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddMember(int teamId, [FromBody] int participantId)
    {
        var team = await _context.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        var participant = await _participantContext.Participants.FindAsync(participantId);
        if (participant == null) return NotFound();

        // Check if already assigned to ANY team in SAME session
        var session = team.Session;
        var alreadyAssigned = await _context.Members
            .AnyAsync(m => m.ParticipantId == participantId && _context.Teams.Any(t => t.Id == m.TeamId && t.Session == session));
        
        if (alreadyAssigned) return BadRequest("이미 해당 부(Session)에 배정된 인원입니다.");

        _context.Members.Add(new VolleyballMember { 
            TeamId = teamId, 
            ParticipantId = participantId, 
            ParticipantName = participant.Name 
        });

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("members/{memberId}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RemoveMember(int memberId)
    {
        var member = await _context.Members.FindAsync(memberId);
        if (member == null) return NotFound();

        _context.Members.Remove(member);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("update-members-batch")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateMembersBatch([FromBody] List<MemberTeamUpdate> updates)
    {
        foreach (var update in updates)
        {
            var member = await _context.Members.FirstOrDefaultAsync(m => m.ParticipantId == update.ParticipantId);
            if (member != null)
            {
                member.TeamId = update.TeamId;
            }
        }
        await _context.SaveChangesAsync();
        return Ok();
    }

    // 3. Mini-game Scoring
    [HttpPost("teams/{teamId}/score-mini")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddMiniGameScore(int teamId, [FromBody] int points)
    {
        var team = await _context.Teams.FindAsync(teamId);
        if (team == null) return NotFound();

        team.MiniGamePoints += points;
        team.Points += points; // Total points
        await _context.SaveChangesAsync();
        return Ok(new { total = team.MiniGamePoints, points = team.Points });
    }

    // 4. Match Management (Manual)
    [HttpPost("matches")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateMatch([FromBody] MatchCreateRequest req)
    {
        var t1 = await _context.Teams.FindAsync(req.Team1Id);
        var t2 = await _context.Teams.FindAsync(req.Team2Id);
        if (t1 == null || t2 == null) return BadRequest("팀을 찾을 수 없습니다.");
        if (t1.Id == t2.Id) return BadRequest("동일한 팀을 매칭할 수 없습니다.");

        var lastMatchOrder = await _context.Matches
            .Where(m => m.Session == req.Session)
            .MaxAsync(m => (int?)m.MatchOrder) ?? 0;

        var match = new VolleyballMatch
        {
            Team1Id = Math.Min(t1.Id, t2.Id),
            Team2Id = Math.Max(t1.Id, t2.Id),
            Session = req.Session,
            MatchOrder = lastMatchOrder + 1,
            Status = MatchStatus.Pending
        };

        _context.Matches.Add(match);
        await _context.SaveChangesAsync();
        return Ok(match);
    }

    [HttpPost("matches/{matchId}/start")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> StartMatch(int matchId)
    {
        var match = await _context.Matches.FindAsync(matchId);
        if (match == null || match.Status != MatchStatus.Pending) return BadRequest("경기를 시작할 수 없는 상태입니다.");

        match.Status = MatchStatus.InProgress;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("sessions/{session}/reset-matches")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetMatches(VolleyballSession session)
    {
        var matches = _context.Matches.Where(m => m.Session == session);
        _context.Matches.RemoveRange(matches);

        var teams = await _context.Teams.Where(t => t.Session == session).ToListAsync();
        foreach (var t in teams)
        {
            t.Wins = 0;
            t.Losses = 0;
            t.Points = t.MiniGamePoints; 
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("matches/{matchId}/result")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SetMatchResult(int matchId, [FromBody] int winnerTeamId)
    {
        var match = await _context.Matches.FindAsync(matchId);
        if (match == null || match.Status == MatchStatus.Completed) return BadRequest("유효하지 않은 경기입니다.");

        match.WinnerTeamId = winnerTeamId;
        match.Status = MatchStatus.Completed;

        var winner = await _context.Teams.FindAsync(winnerTeamId);
        var loserId = match.Team1Id == winnerTeamId ? match.Team2Id : match.Team1Id;
        var loser = await _context.Teams.FindAsync(loserId);

        if (winner != null && loser != null)
        {
            winner.Wins++;
            winner.Points += 1; 
            loser.Losses++;
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    // Delete a specific match (Manual Management)
    [HttpDelete("matches/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteMatch(int id)
    {
        var match = await _context.Matches.FindAsync(id);
        if (match == null) return NotFound();
        if (match.Status == MatchStatus.Completed) return BadRequest("이미 완료된 경기는 삭제할 수 없습니다.");

        _context.Matches.Remove(match);
        await _context.SaveChangesAsync();
        return Ok();
    }

    private (int t1, int t2)? CalculateNextPairing(List<VolleyballTeam> teams, List<VolleyballMatch> allMatches)
    {
        if (teams.Count < 2) return null;

        var currentMatchCount = allMatches.Count;
        var lastMatch = allMatches.OrderByDescending(m => m.MatchOrder).FirstOrDefault();

        // 1. PHASE 1: Full League (Round Robin) Rules
        var unplayedPairings = new List<(int, int)>();
        for (int i = 0; i < teams.Count; i++)
            for (int j = i + 1; j < teams.Count; j++)
                if (!allMatches.Any(m => (m.Team1Id == teams[i].Id && m.Team2Id == teams[j].Id) || (m.Team1Id == teams[j].Id && m.Team2Id == teams[i].Id)))
                    unplayedPairings.Add((teams[i].Id, teams[j].Id));

        if (unplayedPairings.Count > 0)
        {
            // SPECIFIC RULES FOR 4 TEAMS DURING INITIAL LEAGUE
            if (teams.Count == 4)
            {
                if (currentMatchCount == 1) // #2: Match the two teams that haven't played
                {
                    var playedIds = new List<int> { lastMatch!.Team1Id, lastMatch.Team2Id };
                    var unplayed = teams.Where(t => !playedIds.Contains(t.Id)).ToList();
                    if (unplayed.Count == 2) return (Math.Min(unplayed[0].Id, unplayed[1].Id), Math.Max(unplayed[0].Id, unplayed[1].Id));
                }
                else if (currentMatchCount == 2) // #3: Winner vs Winner
                {
                    var winners = allMatches.Where(m => m.WinnerTeamId.HasValue).Select(m => m.WinnerTeamId!.Value).ToList();
                    if (winners.Count == 2) return (Math.Min(winners[0], winners[1]), Math.Max(winners[0], winners[1]));
                }
                else if (currentMatchCount == 3) // #4: Loser vs Loser of first two matches
                {
                    var m1 = allMatches.FirstOrDefault(m => m.MatchOrder == 1);
                    var m2 = allMatches.FirstOrDefault(m => m.MatchOrder == 2);
                    if (m1?.WinnerTeamId != null && m2?.WinnerTeamId != null)
                    {
                        int l1 = m1.Team1Id == m1.WinnerTeamId ? m1.Team2Id : m1.Team1Id;
                        int l2 = m2.Team1Id == m2.WinnerTeamId ? m2.Team2Id : m2.Team1Id;
                        return (Math.Min(l1, l2), Math.Max(l1, l2));
                    }
                }
            }
        }

        // 2. PHASE 2: General Rotation (Strict 3-team logic or post-league)
        var teamStats = teams.ToDictionary(
            t => t.Id,
            t => {
                var matches = allMatches.Where(m => m.Team1Id == t.Id || m.Team2Id == t.Id).OrderByDescending(m => m.MatchOrder).ToList();
                var lastOrder = matches.FirstOrDefault()?.MatchOrder ?? 0;
                int consecutive = 0;
                for (int i = 0; i < matches.Count; i++) {
                    if (matches[i].MatchOrder == currentMatchCount - i) consecutive++;
                    else break;
                }
                return new { Gap = lastOrder == 0 ? 9999 : (currentMatchCount - lastOrder), Consecutive = consecutive };
            }
        );

        var pairingScores = new List<((int t1, int t2) pairing, long score)>();
        foreach (var p in (unplayedPairings.Count > 0 ? unplayedPairings : GenerateAllPairings(teams)))
        {
            long score = 0;
            var s1 = teamStats[p.Item1];
            var s2 = teamStats[p.Item2];

            score += (long)(s1.Gap + s2.Gap) * 1000000000;
            if (s1.Gap == 0) score -= 5000000000;
            if (s2.Gap == 0) score -= 5000000000;
            if (s1.Consecutive >= 2) score -= 10000000000;
            if (s2.Consecutive >= 2) score -= 10000000000;

            // Tie-breaker: Winner Priority (only if gaps are same)
            if (lastMatch?.WinnerTeamId == p.Item1 || lastMatch?.WinnerTeamId == p.Item2) score += 1000000;

            pairingScores.Add((p, score));
        }

        var best = pairingScores.OrderByDescending(s => s.score).First().pairing;
        return (Math.Min(best.t1, best.t2), Math.Max(best.t1, best.t2));
    }

    private List<(int, int)> GenerateAllPairings(List<VolleyballTeam> teams)
    {
        var list = new List<(int, int)>();
        for (int i = 0; i < teams.Count; i++)
            for (int j = i + 1; j < teams.Count; j++)
                list.Add((teams[i].Id, teams[j].Id));
        return list;
    }

    // 5. Visibility
    [HttpPost("toggle-public")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> TogglePublic([FromBody] VolleyballSession session)
    {
        var settings = await _context.Settings.FirstOrDefaultAsync() ?? new VolleyballSettings();
        if (settings.Id == 0) _context.Settings.Add(settings);

        if (session == VolleyballSession.FirstHalf)
        {
            settings.IsFirstHalfPublic = !settings.IsFirstHalfPublic;
            if (settings.IsFirstHalfPublic) settings.IsSecondHalfPublic = false; // Mutually exclusive
        }
        else
        {
            settings.IsSecondHalfPublic = !settings.IsSecondHalfPublic;
            if (settings.IsSecondHalfPublic) settings.IsFirstHalfPublic = false; // Mutually exclusive
        }

        await _context.SaveChangesAsync();
        return Ok(settings);
    }

    [HttpPost("reset")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetData()
    {
        _context.Matches.RemoveRange(_context.Matches);
        _context.Members.RemoveRange(_context.Members);
        _context.Teams.RemoveRange(_context.Teams);
        var s = await _context.Settings.FirstOrDefaultAsync();
        if (s != null) { s.IsFirstHalfPublic = false; s.IsSecondHalfPublic = false; }
        await _context.SaveChangesAsync();
        return Ok();
    }
}

public class MemberTeamUpdate { public int ParticipantId { get; set; } public int TeamId { get; set; } }

public class MatchCreateRequest
{
    public int Team1Id { get; set; }
    public int Team2Id { get; set; }
    public VolleyballSession Session { get; set; }
}
