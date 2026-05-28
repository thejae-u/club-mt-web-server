using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClubMTServer.Services
{
    public class LogCleanupService : BackgroundService
    {
        private readonly ILogger<LogCleanupService> _logger;
        private readonly string _logDirectory;
        private readonly string _archiveDirectory;

        public LogCleanupService(ILogger<LogCleanupService> logger)
        {
            _logger = logger;
            _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data", "logs");
            _archiveDirectory = Path.Combine(_logDirectory, "archive");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Log Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting log cleanup and archive process...");
                    CleanupLogs();
                    _logger.LogInformation("Log cleanup and archive process completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during log cleanup.");
                }

                // Run once every 24 hours
                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("Log Cleanup Service is stopping.");
        }

        private void CleanupLogs()
        {
            if (!Directory.Exists(_logDirectory))
            {
                _logger.LogWarning("Log directory {LogDirectory} does not exist.", _logDirectory);
                return;
            }

            if (!Directory.Exists(_archiveDirectory))
            {
                Directory.CreateDirectory(_archiveDirectory);
            }

            var now = DateTime.Now;
            var logFiles = Directory.GetFiles(_logDirectory, "*.log", SearchOption.AllDirectories);

            foreach (var logFile in logFiles)
            {
                // Skip files already in the archive directory
                if (logFile.StartsWith(_archiveDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileInfo = new FileInfo(logFile);
                if (now - fileInfo.LastWriteTime > TimeSpan.FromDays(30))
                {
                    try
                    {
                        ArchiveFile(logFile);
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning("Could not process file {LogFile}: {Message}. It might be in use.", logFile, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to archive file {LogFile}.", logFile);
                    }
                }
            }
        }

        private void ArchiveFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            var archivePath = Path.Combine(_archiveDirectory, fileName + ".gz");

            _logger.LogInformation("Archiving {LogFile} to {ArchivePath}", filePath, archivePath);

            using (FileStream originalFileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (FileStream compressedFileStream = File.Create(archivePath))
            using (GZipStream compressionStream = new GZipStream(compressedFileStream, CompressionMode.Compress))
            {
                originalFileStream.CopyTo(compressionStream);
            }

            File.Delete(filePath);
        }
    }
}
