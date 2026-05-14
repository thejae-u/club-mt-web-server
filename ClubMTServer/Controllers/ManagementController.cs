using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ManagementController : ControllerBase
{
    private readonly MemberDbContext _memberContext;
    private readonly ParticipantDbContext _participantContext;
    private readonly ClubMTServer.Services.EncryptionService _encryption;

    public ManagementController(MemberDbContext memberContext, ParticipantDbContext participantContext, ClubMTServer.Services.EncryptionService encryption)
    {
        _memberContext = memberContext;
        _participantContext = participantContext;
        _encryption = encryption;
    }

    [HttpPost("import-csv")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ImportCsv(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("파일이 선택되지 않았습니다.");

        using var reader = new StreamReader(file.OpenReadStream());
        var lines = new List<string>();
        while (reader.Peek() >= 0)
        {
            var line = await reader.ReadLineAsync();
            if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
        }

        if (lines.Count <= 1) return BadRequest("CSV 파일에 데이터가 없습니다.");

        // Replace Strategy: Clear existing members
        _memberContext.Members.RemoveRange(_memberContext.Members);

        var newMembers = new List<Member>();
        // Skip header: Name,Generation,Type
        for (int i = 1; i < lines.Count; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;

            var name = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out var generation)) continue;
            
            var typeStr = parts.Length > 2 ? parts[2].Trim() : "Student";
            if (typeStr == "General") typeStr = "Student";
            if (!Enum.TryParse<ParticipantType>(typeStr, true, out var type)) type = ParticipantType.Student;

            newMembers.Add(new Member
            {
                Name = name, // Automatically encrypted by EF Core
                NameHash = _encryption.ComputeHash(name),
                Generation = generation,
                Type = type,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (newMembers.Any())
        {
            _memberContext.Members.AddRange(newMembers);
            await _memberContext.SaveChangesAsync();
        }

        return Ok(new { message = $"{newMembers.Count}명의 명단이 새로 등록되었습니다." });
    }

    [HttpGet("members")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<object>>> GetMembers()
    {
        bool isAdmin = User.Claims.Any(c => c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "admin");
        var members = await _memberContext.Members.ToListAsync();
        return members
            .Select(m => new {
                m.Id,
                m.Name, // Automatically decrypted by EF Core
                m.Generation,
                m.Type,
                PhoneNumber = isAdmin ? m.PhoneNumber : null, // Automatically decrypted by EF Core
                m.CreatedAt
            })
            .OrderBy(m => m.Generation)
            .ThenBy(m => m.Name)
            .ToList();
    }

    [HttpGet("status")]
    public async Task<ActionResult<object>> GetStatus()
    {
        var participants = await _participantContext.Participants.ToListAsync();
        var registeredOnly = participants.Where(p => p.IsRegistered).ToList();
        
        var confirmed = registeredOnly.Where(p => !p.IsWaitlisted).ToList();
        var waitlisted = registeredOnly.Where(p => p.IsWaitlisted).ToList();

        var counts = new
        {
            Student = confirmed.Count(p => p.Type == ParticipantType.Student),
            Alumni = confirmed.Count(p => p.Type == ParticipantType.Alumni),
            Leave = confirmed.Count(p => p.Type == ParticipantType.Leave),
            Military = confirmed.Count(p => p.Type == ParticipantType.Military),
            Other = confirmed.Count(p => p.Type == ParticipantType.Other),
            
            Waitlisted = waitlisted.Count,
            WaitlistedGeneral = waitlisted.Count(p => p.Type != ParticipantType.Military),
            WaitlistedMilitary = waitlisted.Count(p => p.Type == ParticipantType.Military),
            
            Total = registeredOnly.Count
        };

        var cohortCounts = new Dictionary<int, int>();
        for (int i = 1; i <= 7; i++)
        {
            cohortCounts[i] = confirmed.Count(p => p.Generation == i);
        }

        var expectations = registeredOnly
            .Where(p => !string.IsNullOrEmpty(p.OneLineExpectation))
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .Select(p => {
                var decryptedName = p.Name ?? ""; // Already decrypted by EF Core
                var maskedName = decryptedName.Length > 1 
                    ? decryptedName[..1] + new string('*', decryptedName.Length - 2) + decryptedName[^1..]
                    : decryptedName;
                
                return new 
                { 
                    text = p.OneLineExpectation, 
                    author = $"{p.Generation}기 {maskedName}" 
                };
            })
            .ToList();

        return new { counts, expectations, cohortCounts };
    }

    [HttpPost("manitto/missions")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AddMissions([FromBody] List<string> missions)
    {
        // ... (existing implementation)
        if (missions == null || !missions.Any()) return BadRequest("미션 목록이 비어있습니다.");

        var newMissions = missions.Select(m => new ManittoMission { Description = m }).ToList();
        _participantContext.ManittoMissions.AddRange(newMissions);
        await _participantContext.SaveChangesAsync();

        return Ok(new { message = $"{newMissions.Count}개의 미션이 추가되었습니다." });
    }

    [HttpGet("manitto/missions")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<IEnumerable<ManittoMission>>> GetMissions()
    {
        return await _participantContext.ManittoMissions.ToListAsync();
    }

    [HttpDelete("manitto/missions/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteMission(int id)
    {
        var mission = await _participantContext.ManittoMissions.FindAsync(id);
        if (mission == null) return NotFound();

        _participantContext.ManittoMissions.Remove(mission);
        await _participantContext.SaveChangesAsync();
        return Ok(new { message = "미션이 삭제되었습니다." });
    }

    [HttpPost("manitto/match")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> MatchManitto()
    {
        // ... existing match logic
        var participants = await _participantContext.Participants
            .Where(p => p.IsRegistered)
            .ToListAsync();

        if (participants.Count < 2) return BadRequest("마니또 매칭을 위해서는 최소 2명의 참가자가 필요합니다.");

        var missions = await _participantContext.ManittoMissions.ToListAsync();
        if (!missions.Any()) return BadRequest("배정할 미션이 없습니다. 미션을 먼저 등록해주세요.");

        // Secure Shuffle
        var rng = new Random();
        var shuffled = participants.OrderBy(_ => rng.Next()).ToList();

        // Circular Matching
        for (int i = 0; i < shuffled.Count; i++)
        {
            var current = shuffled[i];
            var target = shuffled[(i + 1) % shuffled.Count];
            var randomMission = missions[rng.Next(missions.Count)];

            current.ManittoTargetId = target.Id;
            current.ManittoMissionId = randomMission.Id;
            current.IsManittoMissionComplete = false;
        }

        await _participantContext.SaveChangesAsync();

        return Ok(new { message = $"{shuffled.Count}명의 마니또 매칭이 완료되었습니다." });
    }

    [HttpGet("manitto/assignments")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<IEnumerable<object>>> GetManittoAssignments()
    {
        var participants = await _participantContext.Participants
            .Where(p => p.IsRegistered)
            .ToListAsync();
        
        var missions = await _participantContext.ManittoMissions.ToListAsync();

        return participants.Select(p => new
        {
            p.Id,
            p.Name, // Automatically decrypted by EF Core
            p.Generation,
            p.ManittoTargetId,
            p.ManittoMissionId,
            p.IsManittoMissionComplete,
            TargetName = p.ManittoTargetId != null 
                ? participants.FirstOrDefault(t => t.Id == p.ManittoTargetId)?.Name 
                : null,
            MissionDescription = missions.FirstOrDefault(m => m.Id == p.ManittoMissionId)?.Description
        }).ToList();
    }

    [HttpPut("manitto/assignments/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateManittoAssignment(int id, [FromBody] ManittoUpdateDto dto)
    {
        var p = await _participantContext.Participants.FindAsync(id);
        if (p == null) return NotFound();

        // Update target/mission only if a value is provided OR explicit clear is requested
        if (dto.TargetId != null || dto.IsTargetCleared) p.ManittoTargetId = dto.TargetId;
        if (dto.MissionId != null || dto.IsMissionCleared) p.ManittoMissionId = dto.MissionId;
        if (dto.IsComplete.HasValue) p.IsManittoMissionComplete = dto.IsComplete.Value;
        
        await _participantContext.SaveChangesAsync();
        return Ok(new { message = "마니또 정보가 수정되었습니다." });
    }

    [HttpPost("reset-settings")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetSettings([FromBody] ResetRequestDto req, [FromServices] SettingsDbContext settingsContext)
    {
        var admin = await _memberContext.AdminUsers.FirstOrDefaultAsync(u => u.Username == User.Identity!.Name);
        if (admin == null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.PasswordHash)) return Unauthorized("비밀번호가 일치하지 않습니다.");
        
        settingsContext.Settings.RemoveRange(settingsContext.Settings);
        await settingsContext.SaveChangesAsync();
        return Ok(new { message = "설정이 초기화되었습니다." });
    }

    [HttpPost("reset-participants")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ResetParticipants([FromBody] ResetRequestDto req)
    {
        var admin = await _memberContext.AdminUsers.FirstOrDefaultAsync(u => u.Username == User.Identity!.Name);
        if (admin == null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.PasswordHash)) return Unauthorized("비밀번호가 일치하지 않습니다.");
        
        _participantContext.Participants.RemoveRange(_participantContext.Participants);
        _participantContext.ManittoMissions.RemoveRange(_participantContext.ManittoMissions);
        _participantContext.ManittoReports.RemoveRange(_participantContext.ManittoReports);
        await _participantContext.SaveChangesAsync();
        return Ok(new { message = "모든 참가 정보가 초기화되었습니다." });
    }

    // --- ADMIN ACCOUNT MANAGEMENT ---

    [HttpGet("admins")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<IEnumerable<object>>> GetAdmins()
    {
        var admins = await _memberContext.AdminUsers
            .Select(a => new { a.Id, a.Username, a.CreatedAt })
            .ToListAsync();
        return Ok(admins);
    }

    [HttpPost("admins")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateAdmin([FromBody] AdminCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
            return BadRequest("아이디와 비밀번호를 입력해주세요.");

        if (await _memberContext.AdminUsers.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("이미 존재하는 아이디입니다.");

        var newAdmin = new AdminUser
        {
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            CreatedAt = DateTime.UtcNow
        };

        _memberContext.AdminUsers.Add(newAdmin);
        await _memberContext.SaveChangesAsync();

        return Ok(new { message = "새로운 관리자 계정이 생성되었습니다." });
    }

    [HttpDelete("admins/{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteAdmin(int id)
    {
        var admin = await _memberContext.AdminUsers.FindAsync(id);
        if (admin == null) return NotFound();

        // Prevent self-deletion
        if (admin.Username == User.Identity?.Name)
            return BadRequest("본인의 계정은 삭제할 수 없습니다.");

        _memberContext.AdminUsers.Remove(admin);
        await _memberContext.SaveChangesAsync();

        return Ok(new { message = "관리자 계정이 삭제되었습니다." });
    }
}

public class AdminCreateDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class ResetRequestDto
{
    public string Password { get; set; } = string.Empty;
}

public class ManittoUpdateDto
{
    public int? TargetId { get; set; }
    public bool IsTargetCleared { get; set; }
    public int? MissionId { get; set; }
    public bool IsMissionCleared { get; set; }
    public bool? IsComplete { get; set; }
}
