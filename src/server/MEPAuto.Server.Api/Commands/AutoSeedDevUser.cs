using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace MEPAuto.Server.Api.Commands
{
    /// <summary>
    /// Auto-copy seed users.json + licenses.json từ <c>tools/dev-seed/</c> xuống DataDir
    /// khi server local start ở Development mode và DataDir chưa có user nào.
    ///
    /// Mục đích: member chạy `dotnet run` là login được luôn bằng cùng credentials VPS,
    /// không phải chạy `seed-user` thủ công.
    ///
    /// Seed file chứa HASH BCrypt (không phải plaintext) — pull từ VPS một lần, commit vào repo.
    /// Khi LEAD update user trên VPS → pull lại tools/dev-seed/ → commit → member git pull tự sync.
    ///
    /// CHỈ chạy khi Environment=Development. Production sẽ skip toàn bộ (defense in depth — phòng
    /// trường hợp lỡ deploy build dev lên VPS).
    /// </summary>
    public static class AutoSeedDevUser
    {
        public static void RunIfNeeded(string dataDir, IWebHostEnvironment env, ILogger logger)
        {
            if (!env.IsDevelopment())
            {
                return;
            }

            var usersPath = Path.Combine(dataDir, "users.json");
            var licensesPath = Path.Combine(dataDir, "licenses.json");

            if (File.Exists(usersPath))
            {
                logger.LogDebug("AutoSeedDevUser: {Path} đã có, skip.", usersPath);
                return;
            }

            var seedDir = FindSeedDir();
            if (seedDir == null)
            {
                logger.LogWarning("AutoSeedDevUser: không tìm thấy tools/dev-seed/ — skip auto-seed. " +
                                  "Member có thể chạy `dotnet run -- seed-user` để tạo user thủ công.");
                return;
            }

            var seedUsers = Path.Combine(seedDir, "users.json");
            var seedLicenses = Path.Combine(seedDir, "licenses.json");
            if (!File.Exists(seedUsers))
            {
                logger.LogWarning("AutoSeedDevUser: {Seed} không tồn tại — skip.", seedUsers);
                return;
            }

            Directory.CreateDirectory(dataDir);
            File.Copy(seedUsers, usersPath, overwrite: false);
            if (File.Exists(seedLicenses))
            {
                File.Copy(seedLicenses, licensesPath, overwrite: false);
            }
            logger.LogInformation("AutoSeedDevUser: đã copy seed từ {Seed} → {DataDir}. " +
                                  "Login với credentials VPS production.", seedDir, dataDir);
        }

        private static string? FindSeedDir()
        {
            // Walk up từ working directory tìm tools/dev-seed/users.json
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "tools", "dev-seed");
                if (File.Exists(Path.Combine(candidate, "users.json")))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
