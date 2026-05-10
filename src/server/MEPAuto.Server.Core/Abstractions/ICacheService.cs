using System;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// In-process cache (Phase 1 wrap IMemoryCache; Phase 2 wrap Redis).
    /// Dùng cho rate limit, JWT validation cache, license cache short-lived.
    /// </summary>
    public interface ICacheService
    {
        bool TryGet<T>(string key, out T? value);
        void Set<T>(string key, T value, TimeSpan ttl);
        void Remove(string key);
    }
}
