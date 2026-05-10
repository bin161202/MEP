using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using Newtonsoft.Json;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Phase 1: 1 key = 1 file. Path: /var/mepauto-data/storage/{key-with-slashes-as-folders}.json
    /// Phù hợp ≤ vài nghìn key. Phase 2: swap RedisDataStorageService.
    /// </summary>
    public class JsonFileDataStorageService : IDataStorageService
    {
        private readonly string _basePath;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public JsonFileDataStorageService(string basePath) { _basePath = basePath; }

        private string PathFor(string key)
        {
            var safeKey = key.Replace("\\", "/").TrimStart('/');
            return Path.Combine(_basePath, safeKey + ".json");
        }

        public async Task<T?> Get<T>(string key) where T : class
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return null;
            await _lock.WaitAsync();
            try
            {
                var json = await File.ReadAllTextAsync(path);
                return string.IsNullOrWhiteSpace(json) ? null : JsonConvert.DeserializeObject<T>(json);
            }
            finally { _lock.Release(); }
        }

        public async Task Set<T>(string key, T value) where T : class
        {
            var path = PathFor(key);
            await _lock.WaitAsync();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            }
            finally { _lock.Release(); }
        }

        public Task<bool> Delete(string key)
        {
            var path = PathFor(key);
            if (!File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }
    }
}
