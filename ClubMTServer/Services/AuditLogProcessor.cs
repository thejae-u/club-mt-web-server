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
        private readonly ILogger<AuditLogProcessor> _logger;

        public AuditLogProcessor(AuditLogQueue queue, ILogger<AuditLogProcessor> logger)
        {
            _queue = queue;
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
                    _logger.LogInformation("AuditLog: {@AuditLog}", log);
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
