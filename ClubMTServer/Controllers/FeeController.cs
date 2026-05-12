using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ClubMTServer.Data;
using ClubMTServer.Models;
using System.Threading;

namespace ClubMTServer.Controllers;

[Route("api/[controller]")]
[ApiController]
public class FeeController : ControllerBase
{
    private readonly FeeDbContext _context;
    private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public FeeController(FeeDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FeeHistory>>> GetFees()
    {
        return await _context.FeeHistories.OrderByDescending(f => f.Date).ThenByDescending(f => f.Id).ToListAsync();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<object>> GetSummary()
    {
        var fees = await _context.FeeHistories.ToListAsync();
        var totalExpense = fees.Sum(f => Math.Abs(f.Amount));

        return new { totalExpense };
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<FeeHistory>> PostFee(FeeHistory fee)
    {
        await _semaphore.WaitAsync();
        try
        {
            var lastFee = await _context.FeeHistories
                .OrderByDescending(f => f.Date)
                .ThenByDescending(f => f.Id)
                .FirstOrDefaultAsync();

            int currentBalance = lastFee?.BalanceAfter ?? 0;
            fee.BalanceAfter = currentBalance + fee.Amount;
            fee.Date = fee.Date == default ? DateTime.UtcNow : fee.Date;

            _context.FeeHistories.Add(fee);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFees), new { id = fee.Id }, fee);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteFee(int id)
    {
        await _semaphore.WaitAsync();
        try
        {
            var fee = await _context.FeeHistories.FindAsync(id);
            if (fee == null) return NotFound();

            _context.FeeHistories.Remove(fee);
            await _context.SaveChangesAsync();
            return NoContent();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
