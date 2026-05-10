using System;
using MEPAuto.Server.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Wrap IMemoryCache. Phase 2 swap RedisCacheService — interface không đổi.
    /// </summary>
    public class MemoryCacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        public MemoryCacheService(IMemoryCache cache) { _cache = cache; }

        public bool TryGet<T>(string key, out T? value)
        {
            if (_cache.TryGetValue(key, out var raw) && raw is T t)
            {
                value = t;
                return true;
            }
            value = default;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan ttl)
        {
            _cache.Set(key, value, ttl);
        }

        public void Remove(string key) => _cache.Remove(key);
    }
}
