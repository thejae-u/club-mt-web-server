using ClubMTServer.Data;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ClubMTServer.Models;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Serilog.Filters;

using System.Text.Json.Serialization;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(
        path: "data/logs/api-.log",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Logger(lc => lc
            .Filter.ByExcluding(Matching.FromSource<ClubMTServer.Services.AuditLogProcessor>())
            .WriteTo.File(
                path: "data/logs/api-.log",
                rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
        .WriteTo.Logger(lc => lc
            .Filter.ByIncludingOnly(Matching.FromSource<ClubMTServer.Services.AuditLogProcessor>())
            .WriteTo.File(
                formatter: new Serilog.Formatting.Json.JsonFormatter(),
                path: "data/logs/audit-.log",
                rollingInterval: RollingInterval.Day)));

    // Add services to the container.
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
    
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50MB
    });

    builder.WebHost.ConfigureKestrel(serverOptions =>
    {
        serverOptions.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50MB
    });

    builder.Services.AddOpenApi();
    builder.Services.AddSingleton<ClubMTServer.Services.EncryptionService>();

    // Critical Configuration Validation
    var jwtSecret = builder.Configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET environment variable is missing.");
    var adminKey = builder.Configuration["ADMIN_KEY"] ?? throw new InvalidOperationException("ADMIN_KEY environment variable is missing.");
    var allowedOriginsString = builder.Configuration["ALLOWED_ORIGINS"] ?? throw new InvalidOperationException("ALLOWED_ORIGINS environment variable is missing.");
    var encryptionKey = builder.Configuration["ENCRYPTION_KEY"] ?? throw new InvalidOperationException("ENCRYPTION_KEY environment variable is missing.");

    var key = Encoding.ASCII.GetBytes(jwtSecret);

    builder.Services.AddAuthentication(x =>
        {
            x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(x =>
        {
            x.RequireHttpsMetadata = true; // Enforce HTTPS
            x.SaveToken = true;
            x.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = "ClubMTServer",
                ValidateAudience = true,
                ValidAudience = "ClubMTClient",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            x.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    context.Token = context.Request.Cookies["jwt"];
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization();

    builder.Services.AddMemoryCache(); // Add Memory Cache

    // Rate Limiting Configuration: Hybrid strategy to support NAT (Wi-Fi sharing) environments
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Login: IP-based but with a higher limit (100) to allow a group of users from the same Wi-Fi
        options.AddPolicy("login", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous-login",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100, 
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

        // Submit: User-based if authenticated, otherwise IP-based
        options.AddPolicy("submit", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() 
                              ?? "anonymous-submit",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }));

        // Cheer: User-based if authenticated, otherwise IP-based
        options.AddPolicy("cheer", httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value 
                              ?? httpContext.Connection.RemoteIpAddress?.ToString() 
                              ?? "anonymous-cheer",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1000,
                    Window = TimeSpan.FromSeconds(10),
                    QueueLimit = 0
                }));
    });

    var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
    var uploadsDir = Path.Combine(dataDir, "uploads");
    Directory.CreateDirectory(dataDir);
    Directory.CreateDirectory(uploadsDir);

    builder.Services.AddDbContext<MemberDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "members.db")}"));
    builder.Services.AddDbContext<SettingsDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "settings.db")}"));
    builder.Services.AddDbContext<FeeDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "fees.db")}"));
    builder.Services.AddDbContext<ParticipantDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "participants.db")}"));
    builder.Services.AddDbContext<ModificationDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "modifications.db")}"));
    builder.Services.AddDbContext<CookingBattleDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "cookingbattle.db")}"));
    builder.Services.AddDbContext<VehicleDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "vehicles.db")}"));
    builder.Services.AddDbContext<PhotoDbContext>(options =>
        options.UseSqlite($"Data Source={Path.Combine(dataDir, "photos.db")}"));

    builder.Services.AddSingleton<ClubMTServer.Services.AuditLogQueue>();
    builder.Services.AddHostedService<ClubMTServer.Services.AuditLogProcessor>();
    builder.Services.AddHostedService<ClubMTServer.Services.LogCleanupService>();

    var allowedOrigins = allowedOriginsString.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowedDomains", policy =>
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    var app = builder.Build();

    // Enable Forwarded Headers for Nginx Proxy
    var forwardedOptions = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
    };
    forwardedOptions.KnownIPNetworks.Clear(); // Trust all networks as we are behind a proxy in Docker
    forwardedOptions.KnownProxies.Clear();  // Trust all proxies as we are behind a proxy in Docker
    app.UseForwardedHeaders(forwardedOptions);

    app.UseCors("AllowedDomains");
    app.UseSerilogRequestLogging();
    app.UseMiddleware<ClubMTServer.Middleware.AuditLogMiddleware>();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // Session Validation Middleware: Enforce single-session management with In-Memory Caching
    app.Use(async (context, next) =>
    {
        // Only validate if a JWT cookie exists. If it doesn't, let standard authorization handle it.
        if (context.Request.Cookies.ContainsKey("jwt") && context.User.Identity?.IsAuthenticated == true)
        {
            var sidClaim = context.User.FindFirst("sid")?.Value;
            var roleClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            bool isValid = false;

            if (!string.IsNullOrEmpty(sidClaim) && !string.IsNullOrEmpty(context.User.Identity.Name))
            {
                var cache = context.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var cacheKey = $"session_{roleClaim}_{context.User.Identity.Name}";

                // 1. Try to get valid session ID from cache
                if (cache.TryGetValue(cacheKey, out string? cachedSid))
                {
                    if (cachedSid == sidClaim) isValid = true;
                }
                else
                {
                    // 2. Cache miss: Query Database
                    using var scope = context.RequestServices.CreateScope();
                    string? dbSid = null;

                    if (roleClaim == "admin")
                    {
                        var db = scope.ServiceProvider.GetRequiredService<MemberDbContext>();
                        var username = context.User.Identity.Name;
                        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
                        dbSid = user?.CurrentSessionId;
                    }
                    else if (roleClaim == "Participant")
                    {
                        var db = scope.ServiceProvider.GetRequiredService<ParticipantDbContext>();
                        var idClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                        if (int.TryParse(idClaim, out int id))
                        {
                            var p = await db.Participants.FindAsync(id);
                            dbSid = p?.CurrentSessionId;
                        }
                    }

                    // 3. Validate and update cache
                    if (dbSid != null)
                    {
                        if (dbSid == sidClaim) isValid = true;
                        
                        // Cache the DB result for 2 minutes to reduce load
                        cache.Set(cacheKey, dbSid, TimeSpan.FromMinutes(2));
                    }
                }
            }

            if (!isValid)
            {
                // Clear the stale cookie
                var isLocalhost = context.Request.Host.Host == "localhost" || context.Request.Host.Host == "127.0.0.1";
                context.Response.Cookies.Delete("jwt", new CookieOptions 
                { 
                    Path = "/",
                    HttpOnly = true,
                    Secure = !isLocalhost,
                    SameSite = SameSiteMode.Lax
                });

                // Return 401 immediately for stale sessions to force logout/re-login
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Session expired or logged in from another device.");
                return;
            }
        }
        await next(context);
    });

    // Security Middleware: Origin, Referer, User-Agent
    app.Use(async (context, next) =>
    {
        if (context.Request.Method == "OPTIONS")
        {
            await next(context);
            return;
        }

        var userAgent = context.Request.Headers["User-Agent"].ToString();
        var origin = context.Request.Headers["Origin"].ToString();
        var referer = context.Request.Headers["Referer"].ToString();

        if (string.IsNullOrEmpty(userAgent))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Bot-like activity detected.");
            return;
        }

        bool hasOrigin = !string.IsNullOrEmpty(origin);
        bool hasReferer = !string.IsNullOrEmpty(referer);

        // Detect if request is coming from or going to localhost
        bool isRequestToLocalhost = context.Request.Host.Host == "localhost" || context.Request.Host.Host == "127.0.0.1";
        bool isOriginLocalhost = hasOrigin && (origin.StartsWith("http://localhost") || origin.StartsWith("http://127.0.0.1"));
        bool isRefererLocalhost = hasReferer && (referer.StartsWith("http://localhost") || referer.StartsWith("http://127.0.0.1"));

        // Strict Origin/Referer Validation: Must exactly match allowed origins or be localhost
        bool originOk = !hasOrigin || allowedOrigins.Contains(origin) || isOriginLocalhost;

        // Referer can be a subpath, but the base domain must be exactly in the allowed list
        bool refererOk = true;
        if (hasReferer)
        {
            refererOk = allowedOrigins.Any(o => referer == o || referer.StartsWith(o + "/")) || isRefererLocalhost;
            
            // Additional strict check for non-localhost referers
            if (refererOk && !isRefererLocalhost)
            {
                try
                {
                    var refererUri = new Uri(referer);
                    var refererBase = $"{refererUri.Scheme}://{refererUri.Host}{(refererUri.IsDefaultPort ? "" : $":{refererUri.Port}")}";
                    if (!allowedOrigins.Contains(refererBase)) refererOk = false;
                }
                catch { refererOk = false; }
            }
        }

        if (!originOk || !refererOk)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden: Invalid Origin or Referer. If you are developing locally, ensure your origin is localhost.");
            return;
        }

        await next(context);
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
            Path.Combine(Directory.GetCurrentDirectory(), "data", "uploads")),
        RequestPath = "/uploads",
        OnPrepareResponse = ctx =>
        {
            // Security: Only allow image types to be served from the uploads folder
            var ext = Path.GetExtension(ctx.File.Name).ToLowerInvariant();
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            if (!allowedExtensions.Contains(ext))
            {
                ctx.Context.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Context.Response.Body = Stream.Null; // Prevent content from being sent
            }
            
            // Add cache control for optimized loading
            ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000");
        }
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.MapControllers();

    using (var scope = app.Services.CreateScope())
    {
        var memberDb = scope.ServiceProvider.GetRequiredService<MemberDbContext>();
        var settingsDb = scope.ServiceProvider.GetRequiredService<SettingsDbContext>();
        var feeDb = scope.ServiceProvider.GetRequiredService<FeeDbContext>();
        var participantDb = scope.ServiceProvider.GetRequiredService<ParticipantDbContext>();
        var modificationDb = scope.ServiceProvider.GetRequiredService<ModificationDbContext>();
        var cookingDb = scope.ServiceProvider.GetRequiredService<CookingBattleDbContext>();
        var vehicleDb = scope.ServiceProvider.GetRequiredService<VehicleDbContext>();
        var photoDb = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();

        memberDb.Database.EnsureCreated();
        settingsDb.Database.EnsureCreated();
        feeDb.Database.EnsureCreated();
        participantDb.Database.EnsureCreated();
        modificationDb.Database.EnsureCreated();
        cookingDb.Database.EnsureCreated();
        vehicleDb.Database.EnsureCreated();
        photoDb.Database.EnsureCreated();

        // AUTO-MIGRATION: Fill missing NameHash for existing data
        var encryption = scope.ServiceProvider.GetRequiredService<ClubMTServer.Services.EncryptionService>();

        var membersToFix = memberDb.Members.Where(m => string.IsNullOrEmpty(m.NameHash)).ToList();
        foreach (var m in membersToFix)
        {
            m.NameHash = encryption.ComputeHash(m.Name);
        }

        if (membersToFix.Any()) memberDb.SaveChanges();

        var participantsToFix = participantDb.Participants.Where(p => string.IsNullOrEmpty(p.NameHash)).ToList();
        foreach (var p in participantsToFix)
        {
            p.NameHash = encryption.ComputeHash(p.Name);
        }

        if (participantsToFix.Any()) participantDb.SaveChanges();

        // MANUAL MIGRATION: ManittoReports ClientRequestId
        try { participantDb.Database.ExecuteSqlRaw("ALTER TABLE ManittoReports ADD COLUMN ClientRequestId TEXT NULL;"); } catch { /* Already exists */ }
        try { participantDb.Database.ExecuteSqlRaw("CREATE UNIQUE INDEX IF NOT EXISTS IX_ManittoReports_ClientRequestId ON ManittoReports (ClientRequestId);"); } catch { /* Already exists */ }

        // MANUAL MIGRATION: Single-Session Management (CurrentSessionId)
        try { memberDb.Database.ExecuteSqlRaw("ALTER TABLE AdminUsers ADD COLUMN CurrentSessionId TEXT NULL;"); } catch { /* Already exists */ }
        try { participantDb.Database.ExecuteSqlRaw("ALTER TABLE Participants ADD COLUMN CurrentSessionId TEXT NULL;"); } catch { /* Already exists */ }

        // MANUAL MIGRATION: Multiple Missions and Visibility
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsManittoPublic INTEGER NOT NULL DEFAULT 0;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN ManittoMissionCount INTEGER NOT NULL DEFAULT 3;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsCookingBattlePublic INTEGER NOT NULL DEFAULT 0;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsCookingBattleChefPublic INTEGER NOT NULL DEFAULT 0;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsCookingBattleVotingActive INTEGER NOT NULL DEFAULT 0;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN IsVehicleAssignmentPublic INTEGER NOT NULL DEFAULT 0;"); } catch { /* Already exists */ }
        try { settingsDb.Database.ExecuteSqlRaw("ALTER TABLE Settings ADD COLUMN MaxCheerPerPerson INTEGER NOT NULL DEFAULT 10;"); } catch { /* Already exists */ }
        try { participantDb.Database.ExecuteSqlRaw("ALTER TABLE Participants ADD COLUMN ManittoMissionsJson TEXT NULL;"); } catch { /* Already exists */ }

        // MANUAL MIGRATION: ModificationComments Table
        try 
        { 
            modificationDb.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ModificationComments (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER NOT NULL,
                    Content TEXT NOT NULL,
                    Author TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_ModificationComments_ModificationTasks_TaskId FOREIGN KEY (TaskId) REFERENCES ModificationTasks (Id) ON DELETE CASCADE
                );");
            modificationDb.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ModificationComments_TaskId ON ModificationComments (TaskId);");
        } catch { }

        // MANUAL MIGRATION: ManittoAssignments Table
        try 
        { 
            participantDb.Database.ExecuteSqlRaw(@"
                CREATE TABLE IF NOT EXISTS ManittoAssignments (
                    ParticipantId INTEGER PRIMARY KEY,
                    TargetId INTEGER NULL,
                    MissionId INTEGER NULL,
                    IsMissionComplete INTEGER NOT NULL DEFAULT 0,
                    MissionsJson TEXT NULL,
                    CONSTRAINT FK_ManittoAssignments_Participants_ParticipantId FOREIGN KEY (ParticipantId) REFERENCES Participants (Id) ON DELETE CASCADE
                );");
        } catch { }

        // MANUAL MIGRATION: Participants IsCancelRequested Column
        try 
        { 
            participantDb.Database.ExecuteSqlRaw(@"ALTER TABLE Participants ADD COLUMN IsCancelRequested INTEGER NOT NULL DEFAULT 0;");
        } catch { }

        // SEED ADMIN USER
        if (!memberDb.AdminUsers.Any())
        {
            memberDb.AdminUsers.Add(new AdminUser
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminKey)
            });
            memberDb.SaveChanges();
        }

        // SEED DEFAULT SETTINGS if empty
        if (!settingsDb.Settings.Any())
        {
            settingsDb.Settings.Add(new SiteSettings());
            settingsDb.SaveChanges();
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
