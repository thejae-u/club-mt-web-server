using ClubMTServer.Models;
using ClubMTServer.Services;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ClubMTServer.Middleware
{
    public class AuditLogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AuditLogQueue _queue;

        public AuditLogMiddleware(RequestDelegate next, AuditLogQueue queue)
        {
            _next = next;
            _queue = queue;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var method = context.Request.Method;

            // Only log CUD operations (POST, PUT, PATCH, DELETE)
            if (method == "POST" || method == "PUT" || method == "PATCH" || method == "DELETE")
            {
                // Capture request body
                context.Request.EnableBuffering();
                var requestBody = string.Empty;
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, true))
                {
                    requestBody = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;
                }

                // Call next middleware
                await _next(context);

                // Sanitize sensitive information from the request body
                if (!string.IsNullOrEmpty(requestBody))
                {
                    // Mask common sensitive JSON keys
                    requestBody = Regex.Replace(
                        requestBody, 
                        @"""(password|newPassword|oldPassword|adminKey|key)""\s*:\s*""[^""]*""", 
                        @"""$1"": ""***MASKED***""", 
                        RegexOptions.IgnoreCase);
                }

                // Prepare log entry
                var log = new AuditLog
                {
                    Username = context.User.Identity?.Name ?? "Anonymous",
                    Role = context.User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown",
                    Method = method,
                    Path = context.Request.Path,
                    RequestBody = requestBody,
                    ResponseStatus = context.Response.StatusCode.ToString(),
                    Timestamp = DateTime.UtcNow,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString()
                };

                // Queue the log asynchronously
                await _queue.QueueLogAsync(log);
            }
            else
            {
                await _next(context);
            }
        }
    }
}
