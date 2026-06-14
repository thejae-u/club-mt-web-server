using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClubMTServer.Data;
using ClubMTServer.Models;
using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly MemberDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache;

    public AuthController(MemberDbContext context, IConfiguration configuration, Microsoft.Extensions.Caching.Memory.IMemoryCache cache)
    {
        _context = context;
        _configuration = configuration;
        _cache = cache;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<object>> Login([FromBody] LoginDto login)
    {
        var user = await _context.AdminUsers.FirstOrDefaultAsync(u => u.Username == login.Username);
        
        if (user == null || !BCrypt.Net.BCrypt.Verify(login.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Invalid username or password" });
        }

        // Generate Session ID for single-session management
        var sessionId = Guid.NewGuid().ToString();
        user.CurrentSessionId = sessionId;
        await _context.SaveChangesAsync();

        // IMPORTANT: Invalidate the session cache to ensure the middleware picks up the new SID
        _cache.Remove($"session_admin_{user.Username}");

        var token = GenerateJwtToken(user, sessionId);

        var isLocalhost = Request.Host.Host == "localhost" || Request.Host.Host == "127.0.0.1";

        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isLocalhost,
            SameSite = isLocalhost ? SameSiteMode.Lax : SameSiteMode.None,
            Expires = DateTime.UtcNow.AddDays(7),
            Path = "/"
        });

        return Ok(new { username = user.Username });
    }

    [HttpGet("status")]
    [Authorize]
    public IActionResult CheckStatus()
    {
        // Explicitly check for admin role from claims
        bool isAdmin = User.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin");
        if (!isAdmin) return Unauthorized();

        return Ok(new { username = User.Identity?.Name });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var isLocalhost = Request.Host.Host == "localhost" || Request.Host.Host == "127.0.0.1";
        Response.Cookies.Delete("jwt", new CookieOptions 
        { 
            Path = "/",
            HttpOnly = true,
            Secure = !isLocalhost,
            SameSite = SameSiteMode.Lax
        });
        return Ok(new { message = "Logged out successfully" });
    }

    private string GenerateJwtToken(AdminUser user, string sessionId)
    {
        var key = Encoding.ASCII.GetBytes(_configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is missing."));
        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { 
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, "admin"),
                new Claim("sid", sessionId) // Add session ID claim
            }),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "ClubMTServer",
            Audience = "ClubMTClient",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}

public class LoginDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
