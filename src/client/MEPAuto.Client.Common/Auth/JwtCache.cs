using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// Encrypted local cache cho JWT access + refresh token.
    /// DPAPI scope <see cref="DataProtectionScope.CurrentUser"/> → mỗi Windows user có vault riêng.
    /// File ở <c>%LocalAppData%\MEPAuto\jwt.dat</c>.
    /// </summary>
    public class JwtCache
    {
        public class Payload
        {
            public string AccessToken { get; set; } = "";
            public string RefreshToken { get; set; } = "";
            public DateTime ExpiresAt { get; set; }
            public string Email { get; set; } = "";
            public string UserId { get; set; } = "";
        }

        private readonly string _path;

        public JwtCache(string? overridePath = null)
        {
            _path = overridePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MEPAuto",
                "jwt.dat");
        }

        public string CachePath => _path;

        public void Save(Payload payload)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonConvert.SerializeObject(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path, encrypted);
        }

        public Payload? Load()
        {
            if (!File.Exists(_path)) return null;
            try
            {
                var encrypted = File.ReadAllBytes(_path);
                var bytes = ProtectedData.Unprotect(encrypted, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                var json = Encoding.UTF8.GetString(bytes);
                return JsonConvert.DeserializeObject<Payload>(json);
            }
            catch
            {
                // Cache hỏng (đổi user Windows, file corrupt) → xóa, force re-login
                Clear();
                return null;
            }
        }

        public void Clear()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
}
