using System.Threading.Channels;
using ClubMTServer.Models;

namespace ClubMTServer.Services
{
    public class AuditLogQueue
    {
        private readonly Channel<AuditLog> _queue;

        public AuditLogQueue()
        {
            // Unbounded channel for simplicity, or Bounded for resource control
            _queue = Channel.CreateUnbounded<AuditLog>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public async ValueTask QueueLogAsync(AuditLog log)
        {
            await _queue.Writer.WriteAsync(log);
        }

        public async ValueTask<AuditLog> DequeueAsync(CancellationToken cancellationToken)
        {
            return await _queue.Reader.ReadAsync(cancellationToken);
        }
    }
}
