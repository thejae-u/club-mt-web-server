using ClubMTServer.Services;

namespace ClubMTServer.Services;

public class CheerFlushWorker : BackgroundService
{
    private readonly CheerCacheService _cacheService;
    private readonly ILogger<CheerFlushWorker> _logger;

    public CheerFlushWorker(CheerCacheService cacheService, ILogger<CheerFlushWorker> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cheer Flush Worker is starting.");

        // Initial stats load
        await _cacheService.EnsureInitializedAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Flush every 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                await _cacheService.FlushQueueAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while flushing cheers to DB.");
            }
        }

        // Final flush on shutdown
        _logger.LogInformation("Cheer Flush Worker is stopping. Performing final flush.");
        await _cacheService.FlushQueueAsync();
    }
}
