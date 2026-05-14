using ClubMTServer.Data;
using ClubMTServer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubMTServer.Services
{
    public class AuditLogProcessor : BackgroundService
    {
        private readonly AuditLogQueue _queue;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AuditLogProcessor> _logger;

        public AuditLogProcessor(AuditLogQueue queue, IServiceProvider serviceProvider, ILogger<AuditLogProcessor> logger)
        {
            _queue = queue;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Audit Log Processor is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var log = await _queue.DequeueAsync(stoppingToken);

                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AuditLogDbContext>();
                        dbContext.AuditLogs.Add(log);
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing audit log.");
                }
            }

            _logger.LogInformation("Audit Log Processor is stopping.");
        }
    }
}
