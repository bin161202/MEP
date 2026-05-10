using System;
using System.IO;
using Newtonsoft.Json;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// Config Client-side (server URL, ...). Đọc từ <c>%LocalAppData%\MEPAuto\config.json</c>;
    /// nếu file không tồn tại → dùng default + ghi file lần đầu.
    /// User có thể edit file này thủ công để trỏ tới VPS khác (vd staging).
    /// </summary>
    public class ClientConfig
    {
        // Default URL VPS production. Khi LEAD có domain HTTPS riêng thì đổi sang.
        public string ServerBaseUrl { get; set; } = "http://129.212.230.159:8081";

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MEPAuto", "config.json");

        private const string LegacyDefaultUrl = "https://api.mepauto.local";

        public static ClientConfig Load(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
            {
                var fresh = new ClientConfig();
                fresh.Save(path);
                return fresh;
            }
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<ClientConfig>(json) ?? new ClientConfig();
                if (string.Equals(loaded.ServerBaseUrl, LegacyDefaultUrl, StringComparison.OrdinalIgnoreCase))
                {
                    loaded.ServerBaseUrl = new ClientConfig().ServerBaseUrl;
                    loaded.Save(path);
                }
                return loaded;
            }
            catch
            {
                return new ClientConfig();
            }
        }

        public void Save(string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
