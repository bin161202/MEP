using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace MEPAuto.Client.Common.Updater
{
    /// <summary>
    /// Download MSI từ URL trong VersionInfoDto, verify SHA256, ghi vào %TEMP%.
    /// KHÔNG tự kill Revit — gen .bat schedule chạy MSI sau khi user đóng Revit để Revit save trước.
    ///
    /// Lý do dùng .bat thay vì gọi msiexec trực tiếp: msiexec /i sẽ block process gọi nó cho tới khi
    /// install xong. Gọi từ Revit-context thì Revit sẽ block. Detached .bat → Revit thoát → MSI chạy.
    /// </summary>
    public class UpdateInstaller
    {
        private readonly HttpClient _http;
        public UpdateInstaller(HttpClient? httpClient = null)
        {
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        }

        /// <summary>Trả về path MSI đã download. Throw nếu network/checksum fail.</summary>
        public async Task<string> DownloadAsync(string url, string expectedSha256)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("DownloadUrl rỗng — server có thể chưa update version.json đúng.");

            var msiPath = Path.Combine(Path.GetTempPath(), $"MEPAuto-Update-{Guid.NewGuid():N}.msi");

            using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(msiPath);
                await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            // Verify SHA256 — nếu server có set checksum trong version.json.
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                var actual = ComputeSha256(msiPath);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(msiPath);
                    throw new InvalidOperationException(
                        $"SHA256 mismatch — file MSI có thể bị thay đổi giữa đường. " +
                        $"Expected={expectedSha256}, actual={actual}");
                }
            }
            return msiPath;
        }

        /// <summary>
        /// Schedule MSI install qua .bat detached. Trả về true nếu đã schedule.
        /// User vẫn cần đóng Revit thủ công — script sẽ retry tới khi Revit thoát.
        /// </summary>
        public bool ScheduleInstall(string msiPath, bool quiet = false)
        {
            if (!File.Exists(msiPath)) return false;

            var batPath = Path.Combine(Path.GetTempPath(), $"MEPAuto-Install-{Guid.NewGuid():N}.bat");
            var msiArgs = quiet ? "/qn /norestart" : "/qb /norestart";
            var logPath = Path.Combine(Path.GetTempPath(), "MEPAuto-Update-Install.log");

            var bat = $@"@echo off
echo Cho doi Revit thoat truoc khi cap nhat MEPAuto...
:wait
tasklist /FI ""IMAGENAME eq Revit.exe"" 2>nul | find /I ""Revit.exe"" >nul
if not errorlevel 1 (
  timeout /t 2 /nobreak >nul
  goto wait
)
echo Cai dat MEPAuto...
msiexec /i ""{msiPath}"" {msiArgs} /l*v ""{logPath}""
del ""%~f0""
";
            File.WriteAllText(batPath, bat);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{batPath}\"",
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Minimized,
            };
            Process.Start(psi);
            return true;
        }

        private static string ComputeSha256(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var bytes = sha.ComputeHash(fs);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}
