using System.Threading.Tasks;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// Generic key-value storage cho session, refresh token, cache state.
    /// Phase 1 = JSON file per key; Phase 2 = Redis.
    /// Key tự namespace (vd "refresh:{token-hash}", "session:{userId}:{sessionId}").
    /// </summary>
    public interface IDataStorageService
    {
        Task<T?> Get<T>(string key) where T : class;
        Task Set<T>(string key, T value) where T : class;
        Task<bool> Delete(string key);
    }
}
