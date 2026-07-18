using System.Collections.Concurrent;
using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.EntityFrameworkCore;

namespace ClubMTServer.Services;

public class CheerCacheService
{
    private readonly IServiceProvider _serviceProvider;
    
    // Queue to hold cheer requests: (ParticipantId, Team)
    private readonly ConcurrentQueue<(int ParticipantId, CookingTeam Team)> _cheerQueue = new();
    
    // Cache for user's cheer counts to enforce limits without hitting DB
    private readonly ConcurrentDictionary<int, int> _userCheerCountCache = new();
    
    // Global stats cache
    private int _blackCheerCount = -1;
    private int _whiteCheerCount = -1;
    private bool _isInitialized = false;

    public CheerCacheService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_isInitialized) return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CookingBattleDbContext>();
        
        _blackCheerCount = await db.Cheers.CountAsync(c => c.Team == CookingTeam.Black);
        _whiteCheerCount = await db.Cheers.CountAsync(c => c.Team == CookingTeam.White);
        _isInitialized = true;
    }

    public async Task<int> GetUserCheerCountAsync(int participantId)
    {
        if (_userCheerCountCache.TryGetValue(participantId, out int count))
        {
            return count;
        }

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CookingBattleDbContext>();
        int dbCount = await db.Cheers.CountAsync(c => c.ParticipantId == participantId);
        
        _userCheerCountCache.TryAdd(participantId, dbCount);
        return dbCount;
    }

    public bool TryAddCheer(int participantId, CookingTeam team, int maxCheerPerPerson)
    {
        // Atomically increment and check limit
        int newCount = _userCheerCountCache.AddOrUpdate(participantId, 1, (_, oldValue) => oldValue + 1);
        
        if (newCount > maxCheerPerPerson)
        {
            _userCheerCountCache.AddOrUpdate(participantId, newCount - 1, (_, oldValue) => oldValue - 1);
            return false;
        }

        // Add to persistence queue
        _cheerQueue.Enqueue((participantId, team));

        // Update global stats immediately in memory
        if (team == CookingTeam.Black) Interlocked.Increment(ref _blackCheerCount);
        else if (team == CookingTeam.White) Interlocked.Increment(ref _whiteCheerCount);

        return true;
    }

    public async Task FlushQueueAsync()
    {
        if (_cheerQueue.IsEmpty) return;

        var cheersToSave = new List<CookingBattleCheer>();
        while (_cheerQueue.TryDequeue(out var data))
        {
            cheersToSave.Add(new CookingBattleCheer
            {
                ParticipantId = data.ParticipantId,
                Team = data.Team,
                CreatedAt = DateTime.UtcNow
            });
        }

        if (cheersToSave.Count > 0)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CookingBattleDbContext>();
            db.Cheers.AddRange(cheersToSave);
            await db.SaveChangesAsync();
        }
    }

    public (int Black, int White) GetGlobalStats()
    {
        return (_blackCheerCount < 0 ? 0 : _blackCheerCount, _whiteCheerCount < 0 ? 0 : _whiteCheerCount);
    }

    public void ClearCache()
    {
        while (_cheerQueue.TryDequeue(out _)) { }
        _userCheerCountCache.Clear();
        _blackCheerCount = 0;
        _whiteCheerCount = 0;
        _isInitialized = false;
    }
}
