using ClubMTServer.Models;
using ClubMTServer.Services;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ClubMTServer.Middleware
{
    public class AuditLogMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AuditLogQueue _queue;
        private static readonly string[] SensitiveKeys = { "password", "newPassword", "oldPassword", "adminKey", "key" };

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

                // Sanitize sensitive information from the request body using structured JSON parsing
                var sanitizedBody = requestBody;
                if (!string.IsNullOrEmpty(requestBody))
                {
                    try
                    {
                        var node = JsonNode.Parse(requestBody);
                        if (node != null)
                        {
                            MaskSensitiveData(node);
                            sanitizedBody = node.ToJsonString();
                        }
                    }
                    catch
                    {
                        // If parsing fails (not JSON or malformed), fall back to keeping the original (or we could choose to mask everything)
                        // For safety, if it's not JSON, we might want to log that it couldn't be sanitized.
                    }
                }

                // Prepare log entry
                var log = new AuditLog
                {
                    Username = context.User.Identity?.Name ?? "Anonymous",
                    Role = context.User.FindFirst(ClaimTypes.Role)?.Value ?? "Unknown",
                    Method = method,
                    Path = context.Request.Path,
                    RequestBody = sanitizedBody,
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

        private void MaskSensitiveData(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (var key in SensitiveKeys)
                {
                    if (obj.ContainsKey(key))
                    {
                        obj[key] = JsonValue.Create("***MASKED***");
                    }
                }

                // Recursively check for nested objects or arrays
                foreach (var property in obj)
                {
                    if (property.Value != null)
                    {
                        MaskSensitiveData(property.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        MaskSensitiveData(item);
                    }
                }
            }
        }
    }
}
