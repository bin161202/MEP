using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using Newtonsoft.Json;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Phase 1: license map lưu trong /var/mepauto-data/licenses.json dạng { "email@x": ["feature.a", "feature.b"] }.
    /// Read primary từ JWT claim "licenses" (đã embed lúc login → fast); fallback đọc file.
    /// </summary>
    public class JsonFileLicenseService : ILicenseService
    {
        private readonly string _path;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public JsonFileLicenseService(string path) { _path = path; }

        public Task<bool> CanUse(ClaimsPrincipal user, string feature)
        {
            var claim = user.FindFirst("licenses")?.Value;
            if (!string.IsNullOrEmpty(claim))
            {
                var features = claim!.Split(',');
                return Task.FromResult(features.Contains(feature));
            }
            return CanUseFromFile(user, feature);
        }

        public async Task<string[]> GetFeatures(string email)
        {
            var map = await ReadMap();
            return map.TryGetValue(email, out var features) ? features.ToArray() : System.Array.Empty<string>();
        }

        private async Task<bool> CanUseFromFile(ClaimsPrincipal user, string feature)
        {
            var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(email)) return false;
            var features = await GetFeatures(email!);
            return features.Contains(feature);
        }

        private async Task<Dictionary<string, List<string>>> ReadMap()
        {
            await _lock.WaitAsync();
            try
            {
                if (!File.Exists(_path)) return new Dictionary<string, List<string>>();
                var json = await File.ReadAllTextAsync(_path);
                if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, List<string>>();
                return JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json)
                       ?? new Dictionary<string, List<string>>();
            }
            finally { _lock.Release(); }
        }
    }
}
