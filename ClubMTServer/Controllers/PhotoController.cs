using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ImageMagick;

namespace ClubMTServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PhotoController : ControllerBase
    {
        private readonly PhotoDbContext _context;
        private readonly string _uploadsFolder;

        public PhotoController(PhotoDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _uploadsFolder = Path.Combine(AppContext.BaseDirectory, "data", "uploads");
            if (!Directory.Exists(_uploadsFolder)) Directory.CreateDirectory(_uploadsFolder);
        }

        // --- Public Endpoints ---

        [HttpGet("sessions")]
        public async Task<ActionResult<IEnumerable<PhotoSession>>> GetSessions()
        {
            return await _context.PhotoSessions
                .Include(s => s.Photos)
                .OrderBy(s => s.Order)
                .ThenByDescending(s => s.CreatedAt)
                .ToListAsync();
        }

        [HttpGet("all-photos")]
        public async Task<ActionResult<IEnumerable<Photo>>> GetAllPhotos()
        {
            return await _context.Photos
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        // --- Admin Endpoints ---

        [Authorize(Roles = "admin")]
        [HttpPost("sessions/reorder")]
        public async Task<IActionResult> ReorderSessions([FromBody] List<SessionOrderUpdate> updates)
        {
            foreach (var update in updates)
            {
                var session = await _context.PhotoSessions.FindAsync(update.Id);
                if (session != null)
                {
                    session.Order = update.Order;
                }
            }
            await _context.SaveChangesAsync();
            return NoContent();
        }

        public class SessionOrderUpdate
        {
            public int Id { get; set; }
            public int Order { get; set; }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("sessions")]
        public async Task<ActionResult<PhotoSession>> CreateSession([FromBody] PhotoSession session)
        {
            if (string.IsNullOrWhiteSpace(session.Title)) return BadRequest("Title is required");
            
            _context.PhotoSessions.Add(session);
            await _context.SaveChangesAsync();
            return CreatedAtAction(nameof(GetSessions), new { id = session.Id }, session);
        }

        [Authorize(Roles = "admin")]
        [HttpPut("sessions/{id}")]
        public async Task<IActionResult> UpdateSession(int id, [FromBody] PhotoSession session)
        {
            if (id != session.Id) return BadRequest();
            _context.Entry(session).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SessionExists(id)) return NotFound();
                else throw;
            }
            return NoContent();
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("sessions/{id}")]
        public async Task<IActionResult> DeleteSession(int id)
        {
            var session = await _context.PhotoSessions.FindAsync(id);
            if (session == null) return NotFound();
            _context.PhotoSessions.Remove(session);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [Authorize(Roles = "admin")]
        [HttpPost("sessions/{sessionId}/photos")]
        public async Task<ActionResult<Photo>> AddPhoto(int sessionId, [FromBody] Photo photo)
        {
            if (!SessionExists(sessionId)) return NotFound("Session not found");
            photo.PhotoSessionId = sessionId;
            _context.Photos.Add(photo);
            await _context.SaveChangesAsync();
            return Ok(photo);
        }

        [Authorize(Roles = "admin")]
        [HttpPost("sessions/{sessionId}/upload")]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<List<Photo>>> UploadPhotos(int sessionId, [FromForm] IFormFileCollection files, [FromForm] string? description)
        {
            try
            {
                if (!SessionExists(sessionId)) return NotFound("Session not found");
                if (files == null || files.Count == 0) return BadRequest("No files uploaded");

                var createdPhotos = new List<Photo>();
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".heic", ".heif" };

                foreach (var file in files)
                {
                    if (file.Length == 0) continue;

                    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(ext)) continue;

                    var guid = Guid.NewGuid().ToString();
                    var originalFileName = $"{guid}.jpg";
                    var thumbFileName = $"thumb_{guid}.jpg";
                    
                    var originalPath = Path.Combine(_uploadsFolder, originalFileName);
                    var thumbPath = Path.Combine(_uploadsFolder, thumbFileName);

                    try
                    {
                        using var stream = file.OpenReadStream();
                        using var image = new MagickImage(stream);
                        
                        // 1. Process Original
                        image.Format = MagickFormat.Jpg;
                        image.Quality = 80;
                        // Strip metadata to save space and for privacy, but keep it for original if needed? 
                        // User mentioned packet optimization, let's strip for both to be safe.
                        image.Strip(); 
                        await image.WriteAsync(originalPath);

                        // 2. Process Thumbnail
                        // Resize to max 400px while maintaining aspect ratio
                        var size = new MagickGeometry(400, 400)
                        {
                            IgnoreAspectRatio = false,
                            Greater = true // Only downscale
                        };
                        image.Resize(size);
                        image.Quality = 60;
                        await image.WriteAsync(thumbPath);

                        var photo = new Photo
                        {
                            PhotoSessionId = sessionId,
                            Url = $"/uploads/{originalFileName}",
                            ThumbnailUrl = $"/uploads/{thumbFileName}",
                            Description = description
                        };
                        _context.Photos.Add(photo);
                        createdPhotos.Add(photo);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Image Processing Error for {file.FileName}: {ex.Message}");
                        // If Magick fails (e.g. corrupt file), we don't save to DB
                    }
                }

                await _context.SaveChangesAsync();
                return Ok(createdPhotos);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Upload Error: {ex.Message}");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("photos/{id}")]
        public async Task<IActionResult> DeletePhoto(int id)
        {
            var photo = await _context.Photos.FindAsync(id);
            if (photo == null) return NotFound();

            // Security: Extract ONLY the filename to prevent Path Traversal
            void SafeDelete(string? url)
            {
                if (string.IsNullOrEmpty(url) || !url.StartsWith("/uploads/")) return;
                
                var fileName = Path.GetFileName(url);
                if (string.IsNullOrEmpty(fileName)) return;

                var filePath = Path.Combine(_uploadsFolder, fileName);
                
                // Double check the file is actually inside the uploads folder
                var fullPath = Path.GetFullPath(filePath);
                var uploadsFullPath = Path.GetFullPath(_uploadsFolder);

                if (fullPath.StartsWith(uploadsFullPath) && System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }

            SafeDelete(photo.Url);
            SafeDelete(photo.ThumbnailUrl);

            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        private bool SessionExists(int id)
        {
            return _context.PhotoSessions.Any(e => e.Id == id);
        }
    }
}
