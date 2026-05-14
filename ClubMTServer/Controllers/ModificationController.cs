using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.AspNetCore.Authorization;

namespace ClubMTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class ModificationController : ControllerBase
{
    private readonly ModificationDbContext _context;

    public ModificationController(ModificationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModificationTask>>> GetTasks()
    {
        return await _context.ModificationTasks
            .Include(t => t.Comments)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    [HttpPost]
    public async Task<ActionResult<ModificationTask>> CreateTask(ModificationTask task)
    {
        task.CreatedAt = DateTime.UtcNow;
        task.Status = "Pending";
        task.RequestedBy = User.Identity?.Name ?? "Unknown";
        
        _context.ModificationTasks.Add(task);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTasks), new { id = task.Id }, task);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(int id, ModificationTask taskUpdate)
    {
        var task = await _context.ModificationTasks.FindAsync(id);
        if (task == null) return NotFound();

        task.Title = taskUpdate.Title;
        task.Description = taskUpdate.Description;
        task.Status = taskUpdate.Status;

        if (task.Status == "Completed" && task.CompletedAt == null)
        {
            task.CompletedAt = DateTime.UtcNow;
        }
        else if (task.Status != "Completed")
        {
            task.CompletedAt = null;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] string status)
    {
        var task = await _context.ModificationTasks.FindAsync(id);
        if (task == null) return NotFound();

        task.Status = status;

        if (task.Status == "Completed")
        {
            task.CompletedAt = DateTime.UtcNow;
        }
        else
        {
            task.CompletedAt = null;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        var task = await _context.ModificationTasks.FindAsync(id);
        if (task == null) return NotFound();

        _context.ModificationTasks.Remove(task);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // --- COMMENTS ---

    [HttpGet("{taskId}/comments")]
    public async Task<ActionResult<IEnumerable<ModificationComment>>> GetComments(int taskId)
    {
        return await _context.ModificationComments
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    [HttpPost("{taskId}/comments")]
    public async Task<ActionResult<ModificationComment>> AddComment(int taskId, [FromBody] string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return BadRequest("댓글 내용을 입력해주세요.");

        var task = await _context.ModificationTasks.FindAsync(taskId);
        if (task == null) return NotFound("요청을 찾을 수 없습니다.");

        var comment = new ModificationComment
        {
            TaskId = taskId,
            Content = content,
            Author = User.Identity?.Name ?? "Unknown",
            CreatedAt = DateTime.UtcNow
        };

        _context.ModificationComments.Add(comment);
        await _context.SaveChangesAsync();

        return Ok(comment);
    }

    [HttpDelete("comments/{commentId}")]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var comment = await _context.ModificationComments.FindAsync(commentId);
        if (comment == null) return NotFound();

        // Permission check: Only requester (author) can delete
        if (comment.Author != User.Identity?.Name)
        {
            return Forbid("댓글 삭제 권한이 없습니다. (본인만 삭제 가능)");
        }

        _context.ModificationComments.Remove(comment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
