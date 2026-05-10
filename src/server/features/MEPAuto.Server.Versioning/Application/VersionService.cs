using System;
using System.IO;
using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MEPAuto.Server.Versioning.Application
{
    /// <summary>
    /// Đọc /var/mepauto-data/version.json cho VersionController. LEAD update file này tay khi release MSI mới.
    /// Cache 60s in-memory để không đọc disk mỗi request.
    /// </summary>
    public class VersionService
    {
        private readonly string _versionFile;
        private readonly ILogger<VersionService> _logger;
        private readonly object _lock = new object();
        private VersionFileSchema? _cache;
        private DateTime _cacheLoadedAt = DateTime.MinValue;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        public VersionService(string dataDir, ILogger<VersionService> logger)
        {
            _versionFile = Path.Combine(dataDir, "version.json");
            _logger = logger;
        }

        public Task<VersionInfoDto> GetVersionInfo(string clientCurrent)
        {
            var schema = LoadSchema();
            var info = new VersionInfoDto
            {
                Latest = schema.Latest ?? "0.0.0",
                MinSupported = schema.MinSupported ?? "0.0.0",
                ReleaseNotes = schema.ReleaseNotes ?? "",
                RevitVersions = schema.RevitVersions ?? new System.Collections.Generic.List<string>(),
            };

            if (!string.IsNullOrEmpty(schema.DownloadUrlPattern) && !string.IsNullOrEmpty(info.Latest))
                info.DownloadUrl = schema.DownloadUrlPattern.Replace("{version}", info.Latest);

            if (schema.Sha256ByVersion != null && schema.Sha256ByVersion.TryGetValue(info.Latest, out var sha))
                info.Sha256 = sha;

            info.Mandatory = !string.IsNullOrEmpty(clientCurrent)
                && CompareVersion(clientCurrent, info.MinSupported) < 0;

            return Task.FromResult(info);
        }

        private VersionFileSchema LoadSchema()
        {
            lock (_lock)
            {
                if (_cache != null && DateTime.UtcNow - _cacheLoadedAt < CacheTtl)
                    return _cache;

                if (!File.Exists(_versionFile))
                {
                    _logger.LogWarning("version.json không tồn tại tại {Path} → trả default empty", _versionFile);
                    _cache = new VersionFileSchema();
                    _cacheLoadedAt = DateTime.UtcNow;
                    return _cache;
                }

                try
                {
                    var json = File.ReadAllText(_versionFile);
                    _cache = JsonConvert.DeserializeObject<VersionFileSchema>(json) ?? new VersionFileSchema();
                    _cacheLoadedAt = DateTime.UtcNow;
                    return _cache;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed parse version.json — return cached or empty");
                    return _cache ?? new VersionFileSchema();
                }
            }
        }

        internal static int CompareVersion(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            var ap = a.Split('-')[0].Split('.');
            var bp = b.Split('-')[0].Split('.');
            for (int i = 0; i < Math.Max(ap.Length, bp.Length); i++)
            {
                int ai = i < ap.Length && int.TryParse(ap[i], out var av) ? av : 0;
                int bi = i < bp.Length && int.TryParse(bp[i], out var bv) ? bv : 0;
                if (ai != bi) return ai.CompareTo(bi);
            }
            return 0;
        }

        private class VersionFileSchema
        {
            public string? Latest { get; set; }
            public string? MinSupported { get; set; }
            public string? DownloadUrlPattern { get; set; }
            public System.Collections.Generic.Dictionary<string, string>? Sha256ByVersion { get; set; }
            public string? ReleaseNotes { get; set; }
            public System.Collections.Generic.List<string>? RevitVersions { get; set; }
        }
    }
}
