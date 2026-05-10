using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.Core.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace MEPAuto.Server.Infrastructure.FileSystem
{
    /// <summary>
    /// Phase 1 implementation: lưu user vào /var/mepauto-data/users.json (mảng User).
    /// Đủ cho ≤ 50 user, file lock ngăn race condition.
    /// Phase 2: thay bằng PostgresUserRepository (EF Core), không sửa Application/Domain code.
    /// </summary>
    public class JsonFileUserRepository : IUserRepository
    {
        private readonly string _path;
        private readonly ILogger<JsonFileUserRepository> _logger;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public JsonFileUserRepository(string path, ILogger<JsonFileUserRepository> logger)
        {
            _path = path;
            _logger = logger;
        }

        public async Task<User?> GetByEmail(string email)
        {
            var users = await ReadAll();
            return users.FirstOrDefault(u => string.Equals(u.Email, email, System.StringComparison.OrdinalIgnoreCase));
        }

        public async Task<User?> GetById(string userId)
        {
            var users = await ReadAll();
            return users.FirstOrDefault(u => u.UserId == userId);
        }

        public async Task Save(User user)
        {
            await _lock.WaitAsync();
            try
            {
                var users = await ReadAllNoLock();
                users.RemoveAll(u => u.UserId == user.UserId);
                users.Add(user);
                await WriteAllNoLock(users);
                _logger.LogInformation("User saved: {UserId} ({Email})", user.UserId, user.Email);
            }
            finally
            {
                _lock.Release();
            }
        }

        private async Task<List<User>> ReadAll()
        {
            await _lock.WaitAsync();
            try { return await ReadAllNoLock(); }
            finally { _lock.Release(); }
        }

        private async Task<List<User>> ReadAllNoLock()
        {
            if (!File.Exists(_path)) return new List<User>();
            var json = await File.ReadAllTextAsync(_path);
            if (string.IsNullOrWhiteSpace(json)) return new List<User>();
            return JsonConvert.DeserializeObject<List<User>>(json) ?? new List<User>();
        }

        private async Task WriteAllNoLock(List<User> users)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonConvert.SerializeObject(users, Formatting.Indented);
            await File.WriteAllTextAsync(_path, json);
        }
    }
}
