using System;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Append audit entries dạng JSON-line vào /var/mepauto-data/audit.log.
    /// 1 dòng = 1 entry → tail/grep/jq dễ. Serilog file sink ở Server.Api lo rotate hằng ngày.
    /// </summary>
    public class FileAuditLogger : IAuditLogger
    {
        private readonly string _path;
        private readonly ILogger<FileAuditLogger> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public FileAuditLogger(string path, ILogger<FileAuditLogger> logger)
        {
            _path = path;
            _logger = logger;
        }

        public async Task Log(ClaimsPrincipal? user, string action, object? data = null, string status = "ok", int durationMs = 0)
        {
            var entry = new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                UserId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("sub")?.Value,
                Email = user?.FindFirst(ClaimTypes.Email)?.Value ?? user?.FindFirst("email")?.Value,
                Action = action,
                Status = status,
                DurationMs = durationMs,
            };
            if (data != null)
            {
                var dict = JsonConvert.DeserializeObject<System.Collections.Generic.Dictionary<string, object?>>(
                    JsonConvert.SerializeObject(data));
                if (dict != null) entry.Data = dict;
            }
            var line = JsonConvert.SerializeObject(entry);

            await _lock.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await File.AppendAllTextAsync(_path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit log write failed for action {Action}", action);
            }
            finally { _lock.Release(); }
        }
    }
}
