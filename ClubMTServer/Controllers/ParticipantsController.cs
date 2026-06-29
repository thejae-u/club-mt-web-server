using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ParticipantsController : ControllerBase
{
    private readonly ParticipantDbContext _context;
    private readonly MemberDbContext _memberContext;
    private readonly IConfiguration _config;
    private readonly ClubMTServer.Services.EncryptionService _encryption;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    public ParticipantsController(ParticipantDbContext context, MemberDbContext memberContext, IConfiguration config, ClubMTServer.Services.EncryptionService encryption, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _context = context;
        _memberContext = memberContext;
        _config = config;
        _encryption = encryption;
        _cache = cache;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<object>> Login(ParticipantLoginDto login)
    {
        var normalizedName = login.Name.Trim();
        var nameHash = _encryption.ComputeHash(normalizedName);

        // Find by NameHash and Generation to avoid collisions
        var p = await _context.Participants
            .Where(x => x.NameHash == nameHash && x.Generation == login.Generation)
            .OrderByDescending(x => x.IsRegistered) // Prioritize active records
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (p == null || !BCrypt.Net.BCrypt.Verify(login.Password, p.Password))
        {
            return Unauthorized("이름, 기수 또는 비밀번호가 올바르지 않습니다.");
        }

        // Generate Session ID for single-session management
        var sessionId = Guid.NewGuid().ToString();
        p.CurrentSessionId = sessionId;
        await _context.SaveChangesAsync();

        // IMPORTANT: Invalidate the session cache
        _cache.Remove($"session_Participant_{normalizedName}");

        // Generate JWT
        var secret = _config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is missing.");
        var key = Encoding.ASCII.GetBytes(secret);
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { 
                new Claim(ClaimTypes.NameIdentifier, p.Id.ToString()),
                new Claim(ClaimTypes.Name, normalizedName),
                new Claim(ClaimTypes.Role, "Participant"),
                new Claim("sid", sessionId) // Add session ID claim
            }),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = "ClubMTServer",
            Audience = "ClubMTClient",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        var isLocalhost = Request.Host.Host == "localhost" || Request.Host.Host == "127.0.0.1";

        Response.Cookies.Append("jwt", tokenString, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalhost,
            SameSite = isLocalhost ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = DateTime.UtcNow.AddDays(7),
            Path = "/"
        });
        
        return Ok(new { name = normalizedName });
    }

    [HttpGet("me")]
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<object>> GetMe()
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        return new {
            p.Id, p.Type, 
            p.Name, // Automatically decrypted by EF Core
            p.Generation, 
            p.StudentId, // Automatically decrypted by EF Core
            p.PhoneNumber, // Automatically decrypted by EF Core
            p.ParticipationCount, p.MemoryOrExpectation,
            p.ParticipationSchedule, p.MidJoinDetails, p.Transportation,
            p.IsCarpoolAvailable, p.CarpoolSeats,
            p.DepartureArea, p.Allergies, p.Remarks, p.OneLineExpectation,
            p.HasDriverLicense, p.DriverLicenseType, p.CanDrive, p.DrivingExperience,
            p.IsMilitaryPriority, p.MilitaryStatus, p.IsRegistered, p.IsWaitlisted,
            p.IsDepositConfirmed, p.IsCancelRequested, p.ChecklistJson,
            p.CommonChecklistStatusJson, p.MbtiResult
        };
    }

    [HttpPut("me/mbti")]
    [Authorize]
    public async Task<IActionResult> UpdateMbtiResult([FromBody] string mbtiResult)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        p.MbtiResult = mbtiResult;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("me/checklist")]
    [Authorize]
    public async Task<IActionResult> UpdateChecklist([FromBody] string checklistJson)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        p.ChecklistJson = checklistJson;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("me/common-checklist-status")]
    [Authorize]
    public async Task<IActionResult> UpdateCommonChecklistStatus([FromBody] string statusJson)
    {
        var idClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (idClaim == null || !int.TryParse(idClaim, out var id)) return Unauthorized();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        p.CommonChecklistStatusJson = statusJson;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<object>>> GetParticipants()
    {
        // Use more robust check for admin role
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");

        var rawList = await _context.Participants
            .OrderBy(p => p.IsWaitlisted)
            .ThenBy(p => p.SortOrder)
            .ToListAsync();

        if (isAdmin)
        {
            // Admins get ALL data (except password) for management
            return rawList.Select(p => new {
                    p.Id, p.Type, 
                    p.Name, // Automatically decrypted by EF Core
                    p.Generation, 
                    p.StudentId, // Automatically decrypted by EF Core
                    p.PhoneNumber, // Automatically decrypted by EF Core
                    p.ParticipationCount, 
                    p.MemoryOrExpectation,
                    p.ParticipationSchedule, p.MidJoinDetails,
                    p.Transportation, p.IsCarpoolAvailable, p.CarpoolSeats,
                    p.DepartureArea, p.Allergies, p.Remarks,
                    p.OneLineExpectation,
                    p.HasDriverLicense, p.DriverLicenseType, p.CanDrive, p.DrivingExperience,
                    p.IsMilitaryPriority, p.MilitaryStatus,
                    p.IsRegistered, p.IsDepositConfirmed, p.IsWaitlisted, p.IsCancelRequested,
                    p.CreatedAt
                }).ToList<object>();
        }

        return rawList.Select(p => {
                var decryptedName = p.Name ?? ""; // Already decrypted by EF Core
                string maskedName;
                if (decryptedName.Length <= 1)
                {
                    maskedName = decryptedName;
                }
                else if (decryptedName.Length == 2)
                {
                    maskedName = decryptedName[..1] + "*";
                }
                else
                {
                    maskedName = decryptedName[..1] + new string('*', decryptedName.Length - 2) + decryptedName[^1..];
                }

                return (object)new {
                    p.Id, p.Type, 
                    Name = maskedName, 
                    p.Generation, 
                    p.ParticipationCount, 
                    p.OneLineExpectation, p.CreatedAt,
                    p.IsRegistered, p.IsCancelRequested,
                    p.IsDepositConfirmed,
                    p.IsWaitlisted
                };
            }).ToList();
    }

    // POST: api/Participants/5/toggle-waitlist (ADMIN ONLY)
    [HttpPost("{id}/toggle-waitlist")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ToggleWaitlist(int id, [FromServices] SettingsDbContext settingsContext)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var participant = await _context.Participants.FindAsync(id);
            if (participant == null) return NotFound();

            if (participant.IsDepositConfirmed)
            {
                return BadRequest("이미 입금완료된 신청자입니다. 상태를 변경하려면 먼저 입금 확인을 해제해주세요.");
            }

            if (participant.IsWaitlisted)
            {
                // Try to confirm: Check capacity
                var settings = await settingsContext.Settings.FirstOrDefaultAsync() ?? new SiteSettings();
                var allRegistered = await _context.Participants.Where(p => p.IsRegistered && !p.IsWaitlisted && !p.IsCancelRequested).ToListAsync();
                
                if (participant.Type == ParticipantType.Military)
                {
                    if (allRegistered.Count(p => p.Type == ParticipantType.Military) >= settings.MaxMilitaryCapacity)
                        return BadRequest("군인 정원이 가득 찼습니다. 먼저 정원을 늘려주세요.");
                }
                else
                {
                    if (allRegistered.Count(p => p.Type != ParticipantType.Military) >= settings.MaxGeneralCapacity)
                        return BadRequest("일반 정원이 가득 찼습니다. 먼저 정원을 늘려주세요.");
                }
                participant.IsWaitlisted = false;
                // When moving from Waitlisted to Confirmed, we keep the SortOrder (they stay at their relative position)
                // or we could move them to the end of confirmed. 
                // For now, keeping SortOrder is fine as it's already higher than most confirmed.
            }
            else
            {
                // Move back to waitlist: Place at the VERY FRONT of the waitlist
                participant.IsWaitlisted = true;
                
                var minWaitlistedSortOrder = await _context.Participants
                    .Where(p => p.IsWaitlisted && p.Id != id)
                    .MinAsync(p => (double?)p.SortOrder);

                if (minWaitlistedSortOrder.HasValue)
                {
                    participant.SortOrder = minWaitlistedSortOrder.Value - 1.0;
                }
                else
                {
                    // If no one is waitlisted, set it to a value larger than any confirmed but smaller than future ones?
                    // Actually, just making it slightly larger than the max confirmed is better.
                    var maxConfirmedSortOrder = await _context.Participants
                        .Where(p => !p.IsWaitlisted && p.Id != id)
                        .MaxAsync(p => (double?)p.SortOrder);
                    
                    participant.SortOrder = (maxConfirmedSortOrder ?? 0) + 1.0;
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { isWaitlisted = participant.IsWaitlisted, sortOrder = participant.SortOrder });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    // POST: api/Participants/5/toggle-deposit (ADMIN ONLY)
    [HttpPost("{id}/toggle-deposit")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ToggleDeposit(int id)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        participant.IsDepositConfirmed = !participant.IsDepositConfirmed;
        await _context.SaveChangesAsync();

        return Ok(new { isDepositConfirmed = participant.IsDepositConfirmed });
    }

    [HttpPost("{id}/reorder")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ReorderParticipant(int id, [FromBody] int newRank)
    {
        if (newRank <= 0) return BadRequest("Rank must be greater than 0.");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var participant = await _context.Participants.FindAsync(id);
            if (participant == null) return NotFound();

            // Get all participants ordered by SortOrder
            var all = await _context.Participants
                .OrderBy(p => p.SortOrder)
                .ToListAsync();

            all.RemoveAll(p => p.Id == id); // Remove current to calculate new position

            if (newRank == 1)
            {
                participant.SortOrder = (all.Count > 0 ? all[0].SortOrder : 0) - 1.0;
            }
            else if (newRank > all.Count)
            {
                participant.SortOrder = (all.Count > 0 ? all[^1].SortOrder : 0) + 1.0;
            }
            else
            {
                // Place between newRank-1 and newRank (0-indexed in 'all' list)
                // Since we removed 'id', newRank 2 means it should be between all[0] and all[1]
                var before = all[newRank - 2];
                var after = all[newRank - 1];
                participant.SortOrder = (before.SortOrder + after.SortOrder) / 2.0;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok(new { id = participant.Id, newSortOrder = participant.SortOrder });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<ActionResult<object>> GetParticipant(int id)
    {
        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        // Security: Only Admin or the owner can see full details
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isOwner = userIdClaim != null && userIdClaim == id.ToString();

        if (!isAdmin && !isOwner)
        {
            return Forbid();
        }

        return new {
            p.Id, p.Type, 
            p.Name, // Automatically decrypted by EF Core
            p.Generation, 
            p.StudentId, // Automatically decrypted by EF Core
            p.PhoneNumber, // Automatically decrypted by EF Core
            p.ParticipationCount, p.MemoryOrExpectation,
            p.ParticipationSchedule, p.MidJoinDetails, p.Transportation,
            p.IsCarpoolAvailable, p.CarpoolSeats,
            p.DepartureArea, p.Allergies, p.Remarks, p.OneLineExpectation,
            p.HasDriverLicense, p.DriverLicenseType, p.CanDrive, p.DrivingExperience,
            p.IsMilitaryPriority, p.MilitaryStatus, p.IsRegistered, p.IsWaitlisted, p.IsDepositConfirmed
        };
    }

    [HttpPost]
    [EnableRateLimiting("submit")]
    public async Task<ActionResult<object>> PostParticipant(ParticipantRegistrationDto reg, [FromServices] SettingsDbContext settingsContext)
    {
        var settings = await settingsContext.Settings.FirstOrDefaultAsync();
        if (settings == null) return StatusCode(500, "서버 설정 오류입니다.");

        // 신청 활성화(IsRegistrationActive) 확인
        // - 관리자가 강제로 켰거나 (IsRegistrationActive == true)
        // - 현재 시간이 오픈 설정 시간(OpeningDate)을 지났을 때만 허용
        if (!settings.IsRegistrationActive && DateTime.Now < settings.OpeningDate)
        {
            return BadRequest("현재 MT 참가 신청 기간이 아닙니다. 정식 오픈 후에 신청해 주세요.");
        }

        var normalizedName = reg.Name.Trim();
        var nameHash = _encryption.ComputeHash(normalizedName);

        // 1. Find the member record by NameHash and Generation in Member DB
        var member = await _memberContext.Members.FirstOrDefaultAsync(m => 
            m.NameHash == nameHash && 
            m.Generation == reg.Generation);

        if (member == null)
        {
            return BadRequest("해당 이름과 기수로 등록된 동문 정보를 찾을 수 없습니다. 명단에 있는지 확인해주세요.");
        }

        // 2. Check for existing registration in Participant DB
        var existing = await _context.Participants.FirstOrDefaultAsync(p => p.NameHash == nameHash && p.Generation == reg.Generation);
        
        if (existing != null && existing.IsRegistered)
        {
            return BadRequest("이미 신청이 완료된 회원입니다.");
        }

        if (string.IsNullOrEmpty(reg.Password))
        {
            return BadRequest("비밀번호는 필수 입력 항목입니다.");
        }

        if (reg.Password.Length < 8 || !reg.Password.Any(char.IsLetter) || !reg.Password.Any(char.IsDigit))
        {
            return BadRequest("비밀번호는 8자리 이상이며, 영문과 숫자를 모두 포함해야 합니다.");
        }

        // 3. Normalize phone and set password
        var normalizedPhone = new string(reg.PhoneNumber.Where(char.IsDigit).ToArray());
        var finalPassword = reg.Password;

        // 4. Waitlist Logic
        // settings is already loaded at the top of the method
        bool shouldWaitlist = false;
        
        var allRegistered = await _context.Participants.Where(p => p.IsRegistered && !p.IsWaitlisted && !p.IsCancelRequested).ToListAsync();
        if (reg.Type == ParticipantType.Military)
        {
            if (allRegistered.Count(p => p.Type == ParticipantType.Military) >= settings.MaxMilitaryCapacity) shouldWaitlist = true;
        }
        else
        {
            if (allRegistered.Count(p => p.Type != ParticipantType.Military) >= settings.MaxGeneralCapacity) shouldWaitlist = true;
        }

        // 5. Reuse record or create new
        var participant = existing ?? new Participant();

        participant.Name = reg.Name;
        participant.NameHash = nameHash;
        participant.Generation = reg.Generation;
        participant.Type = reg.Type;
        participant.PhoneNumber = normalizedPhone;
        participant.Password = BCrypt.Net.BCrypt.HashPassword(finalPassword);
        participant.StudentId = reg.StudentId;
        participant.ParticipationCount = reg.ParticipationCount;
        participant.MemoryOrExpectation = reg.MemoryOrExpectation;
        participant.ParticipationSchedule = reg.ParticipationSchedule;
        participant.MidJoinDetails = reg.MidJoinDetails;
        participant.Transportation = reg.Transportation;
        participant.IsCarpoolAvailable = reg.IsCarpoolAvailable;
        participant.CarpoolSeats = reg.CarpoolSeats;
        participant.DepartureArea = reg.DepartureArea;
        participant.Allergies = reg.Allergies;
        participant.Remarks = reg.Remarks;
        participant.OneLineExpectation = reg.OneLineExpectation;
        participant.HasDriverLicense = reg.HasDriverLicense;
        participant.DriverLicenseType = reg.DriverLicenseType;
        participant.CanDrive = reg.CanDrive;
        participant.DrivingExperience = reg.DrivingExperience;
        participant.IsMilitaryPriority = reg.Type == ParticipantType.Military;
        participant.MilitaryStatus = reg.MilitaryStatus;
        participant.IsRegistered = true;
        participant.IsWaitlisted = shouldWaitlist;
        participant.CreatedAt = DateTime.UtcNow;
        participant.SortOrder = participant.CreatedAt.Ticks;

        if (existing == null) _context.Participants.Add(participant);

        try 
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest("이미 신청이 진행 중이거나 완료된 회원입니다.");
        }

        string msg = shouldWaitlist ? "정원이 초과되어 대기자 명단으로 등록되었습 니다!" : "신청이 완료되었습니다!";
        return Ok(new { participant.Id, Name = reg.Name, isWaitlisted = shouldWaitlist, message = msg });
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> PutParticipant(int id, [FromBody] ParticipantUpdateDto update)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        // Security: Only Admin or the owner can update
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isOwner = userIdClaim != null && userIdClaim == id.ToString();

        if (!isAdmin && !isOwner)
        {
            return Forbid();
        }

        // SECURITY: If NOT Admin, verify CurrentPassword before any sensitive update
        if (!isAdmin)
        {
            if (string.IsNullOrEmpty(update.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(update.CurrentPassword, participant.Password))
            {
                return Unauthorized("기존 비밀번호가 일치하지 않습니다.");
            }
        }

        var normalizedName = update.Name.Trim();
        participant.Name = update.Name; // Automatically encrypted by EF Core
        participant.NameHash = _encryption.ComputeHash(normalizedName);
        participant.Generation = update.Generation;
        participant.Type = update.Type;
        participant.StudentId = update.StudentId; // Automatically encrypted by EF Core
        participant.PhoneNumber = new string(update.PhoneNumber.Where(char.IsDigit).ToArray()); // Automatically encrypted by EF Core
        participant.ParticipationCount = update.ParticipationCount;
        participant.MemoryOrExpectation = update.MemoryOrExpectation;
        participant.ParticipationSchedule = update.ParticipationSchedule ?? "Full";
        participant.MidJoinDetails = update.MidJoinDetails;
        participant.Transportation = update.Transportation ?? "Car";
        participant.IsCarpoolAvailable = update.IsCarpoolAvailable;
        participant.CarpoolSeats = update.CarpoolSeats;
        participant.DepartureArea = update.DepartureArea;
        participant.Allergies = update.Allergies;
        participant.Remarks = update.Remarks;
        participant.OneLineExpectation = update.OneLineExpectation;
        participant.HasDriverLicense = update.HasDriverLicense;
        participant.DriverLicenseType = update.DriverLicenseType;
        participant.CanDrive = update.CanDrive;
        participant.DrivingExperience = update.DrivingExperience;
        participant.IsMilitaryPriority = update.IsMilitaryPriority;
        participant.MilitaryStatus = update.MilitaryStatus;

        // Admin-only fields OR preserved for owner
        if (isAdmin)
        {
            participant.IsRegistered = update.IsRegistered;
            participant.IsWaitlisted = update.IsWaitlisted;
            participant.IsDepositConfirmed = update.IsDepositConfirmed;
        }
        else
        {
            // If a cancelled user is re-registering
            if (!participant.IsRegistered && update.IsRegistered)
            {
                participant.IsRegistered = true;
                participant.CreatedAt = DateTime.UtcNow;
                // Re-calculate waitlist would go here, but for now we keep it simple
            }
        }

        // NEW: Update password if provided
        bool passwordChanged = false;
        if (!string.IsNullOrEmpty(update.Password))
        {
            if (update.Password.Length < 8 || !update.Password.Any(char.IsLetter) || !update.Password.Any(char.IsDigit))
            {
                return BadRequest("비밀번호는 8자리 이상이며, 영문과 숫자를 모두 포함해야 합니다.");
            }
            participant.Password = BCrypt.Net.BCrypt.HashPassword(update.Password);
            passwordChanged = true;
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!ParticipantExists(id)) return NotFound();
            else throw;
        }

        if (passwordChanged && !isAdmin)
        {
            return Ok(new { passwordChanged = true, message = "비밀번호가 변경되었습니다. 다시 로그인해주세요." });
        }

        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    [Authorize]
    public async Task<IActionResult> ResetPassword(int id)
    {
        // Only admins can reset passwords
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        if (!isAdmin) return Forbid();

        var p = await _context.Participants.FindAsync(id);
        if (p == null) return NotFound();

        var phone = p.PhoneNumber ?? "";
        var normalizedPhone = new string(phone.Where(char.IsDigit).ToArray());
        var last4Digits = normalizedPhone.Length >= 4 ? normalizedPhone[^4..] : "0000";
        var newPassword = $"mt{last4Digits}!!";
        
        p.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"비밀번호가 초기화되었습니다. (초기 비밀번호: {newPassword})" });
    }

    // POST: api/Participants/5/cancel (For Participant Self-Cancellation or Admin Approval)
    [HttpPost("{id}/cancel")]
    [Authorize]
    public async Task<IActionResult> CancelRegistration(int id, [FromBody] CancelRequestDto? request)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        // Security: Only Admin or the owner can cancel
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isOwner = userIdClaim != null && userIdClaim == id.ToString();

        if (!isAdmin && !isOwner)
        {
            return Forbid();
        }

        // Additional Security: If NOT admin, verify password if provided
        if (!isAdmin && request != null && !string.IsNullOrEmpty(request.Password))
        {
            if (!BCrypt.Net.BCrypt.Verify(request.Password, participant.Password))
            {
                return Unauthorized("비밀번호가 일치하지 않습니다.");
            }
        }

        if (!participant.IsRegistered)
        {
            return BadRequest("이미 신청되지 않은 상태입니다.");
        }

        if (!isAdmin && participant.IsDepositConfirmed)
        {
            return BadRequest("입금이 완료된 상태에서는 신청 취소가 불가능합니다. 관리자에게 문의해주세요.");
        }

        if (!isAdmin)
        {
            // 사용자의 요청인 경우: 실제 취소가 아닌 '취소 요청' 상태로만 변경
            participant.IsCancelRequested = true;
            await _context.SaveChangesAsync();
            return Ok(new { message = "취소 요청이 접수되었습니다. 관리자 승인 후 처리됩니다." });
        }
        else
        {
            // 관리자의 요청인 경우: 기존의 완전 취소 로직 수행
            participant.IsRegistered = false;
            participant.IsCancelRequested = false;
            participant.StudentId = _encryption.Encrypt("");
            participant.ParticipationCount = 0;
            participant.MemoryOrExpectation = null;
            participant.OneLineExpectation = null;
            participant.MidJoinDetails = null;
            participant.Transportation = "Walk";
            participant.IsCarpoolAvailable = false;
            participant.CarpoolSeats = 0;
            participant.DepartureArea = null;
            participant.Allergies = null;
            participant.Remarks = null;
            participant.IsMilitaryPriority = false;
            participant.MilitaryStatus = null;
            participant.CreatedAt = DateTime.UtcNow; // Record cancellation time or reset

            await _context.SaveChangesAsync();
            return Ok(new { message = "신청이 취소 처리되었습니다." });
        }
    }

    // POST: api/Participants/5/reject-cancel (ADMIN ONLY)
    [HttpPost("{id}/reject-cancel")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> RejectCancelRequest(int id, [FromServices] SettingsDbContext settingsContext)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        // Re-calculate waitlist
        var settings = await settingsContext.Settings.FirstOrDefaultAsync() ?? new SiteSettings();
        var allRegistered = await _context.Participants.Where(p => p.IsRegistered && !p.IsWaitlisted && !p.IsCancelRequested && p.Id != participant.Id).ToListAsync();
        
        bool shouldWaitlist = false;
        if (participant.Type == ParticipantType.Military)
        {
            if (allRegistered.Count(p => p.Type == ParticipantType.Military) >= settings.MaxMilitaryCapacity) shouldWaitlist = true;
        }
        else
        {
            if (allRegistered.Count(p => p.Type != ParticipantType.Military) >= settings.MaxGeneralCapacity) shouldWaitlist = true;
        }

        participant.IsCancelRequested = false;
        // Default: Add to the end of its group using current ticks
        participant.CreatedAt = DateTime.UtcNow;
        participant.SortOrder = participant.CreatedAt.Ticks;
        participant.IsWaitlisted = shouldWaitlist;

        await _context.SaveChangesAsync();

        return Ok(new { message = "취소 요청이 반려되었습니다. 해당 참가자는 최신 순서로 재배정되었습니다." });
    }

    // POST: api/Participants/5/undo-cancel (For Participant to undo their own cancel request)
    [HttpPost("{id}/undo-cancel")]
    [Authorize]
    public async Task<IActionResult> UndoCancelRequest(int id, [FromBody] CancelRequestDto? request, [FromServices] SettingsDbContext settingsContext)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        bool isOwner = userIdClaim != null && userIdClaim == id.ToString();

        if (!isAdmin && !isOwner)
        {
            return Forbid();
        }

        if (!isAdmin && request != null && !string.IsNullOrEmpty(request.Password))
        {
            if (!BCrypt.Net.BCrypt.Verify(request.Password, participant.Password))
            {
                return Unauthorized("비밀번호가 일치하지 않습니다.");
            }
        }

        if (!participant.IsCancelRequested)
        {
            return BadRequest("취소 요청 상태가 아닙니다.");
        }

        // Re-calculate waitlist as they are "re-entering"
        var settings = await settingsContext.Settings.FirstOrDefaultAsync() ?? new SiteSettings();
        var allRegistered = await _context.Participants.Where(p => p.IsRegistered && !p.IsWaitlisted && !p.IsCancelRequested && p.Id != participant.Id).ToListAsync();
        
        bool shouldWaitlist = false;
        if (participant.Type == ParticipantType.Military)
        {
            if (allRegistered.Count(p => p.Type == ParticipantType.Military) >= settings.MaxMilitaryCapacity) shouldWaitlist = true;
        }
        else
        {
            if (allRegistered.Count(p => p.Type != ParticipantType.Military) >= settings.MaxGeneralCapacity) shouldWaitlist = true;
        }

        participant.IsCancelRequested = false;
        // Default: Add to the end of its group using current ticks
        participant.CreatedAt = DateTime.UtcNow;
        participant.SortOrder = participant.CreatedAt.Ticks;
        participant.IsWaitlisted = shouldWaitlist;

        await _context.SaveChangesAsync();

        string msg = shouldWaitlist 
            ? "취소 요청이 철회되었으나, 그 사이 정원이 초과되어 대기자 명단으로 등록되었습니다." 
            : "취소 요청이 철회되었습니다. 다시 정상 신청 상태로 변경되었습니다.";

        return Ok(new { message = msg, isWaitlisted = shouldWaitlist });
    }

    // DELETE: api/Participants/5 (ADMIN ONLY)
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteParticipant(int id)
    {
        var participant = await _context.Participants.FindAsync(id);
        if (participant == null) return NotFound();

        _context.Participants.Remove(participant);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool ParticipantExists(int id)
    {
        return _context.Participants.Any(e => e.Id == id);
    }
}

public class CancelRequestDto
{
    public string? Password { get; set; }
}

public class ParticipantRegistrationDto
{
    public string Name { get; set; } = string.Empty;
    public int Generation { get; set; }

    public string Password { get; set; } = string.Empty; // Hashed password
    public ParticipantType Type { get; set; }

    [RegularExpression(@"^\d{4}\d{5}$", ErrorMessage = "학번 형식이 올바르지 않습니다. (예: 202612345)")]
    public string? StudentId { get; set; }

    [RegularExpression(@"^010\d{7,8}$", ErrorMessage = "올바른 연락처 형식이 아닙니다. (010으로 시작하는 10~11자리 숫자)")]
    public string PhoneNumber { get; set; } = string.Empty;
    public int ParticipationCount { get; set; }
    public string? MemoryOrExpectation { get; set; }
    public string ParticipationSchedule { get; set; } = "Full";
    public string? MidJoinDetails { get; set; }
    public string Transportation { get; set; } = "Walk";
    public bool IsCarpoolAvailable { get; set; }
    public int CarpoolSeats { get; set; }
    public string? DepartureArea { get; set; }
    public string? Allergies { get; set; }
    public string? Remarks { get; set; }
    public string? OneLineExpectation { get; set; }
    public bool HasDriverLicense { get; set; }
    public string? DriverLicenseType { get; set; }
    public bool CanDrive { get; set; }
    public string? DrivingExperience { get; set; }
    public bool IsMilitaryPriority { get; set; }
    public string? MilitaryStatus { get; set; }
}

public class ParticipantUpdateDto : ParticipantRegistrationDto
{
    public bool IsRegistered { get; set; }
    public bool IsWaitlisted { get; set; }
    public bool IsDepositConfirmed { get; set; }
    public string? CurrentPassword { get; set; }
}

public class ParticipantLoginDto
{
    public string Name { get; set; } = string.Empty;
    public int Generation { get; set; }
    public string Password { get; set; } = string.Empty;
}
