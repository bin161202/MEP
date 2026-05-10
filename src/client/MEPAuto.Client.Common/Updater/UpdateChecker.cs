using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using Newtonsoft.Json;

namespace MEPAuto.Client.Common.Updater
{
    /// <summary>
    /// Background check version mỗi lần Revit start. Gọi /api/v1/version/check anonymous
    /// (không qua ServerProxy vì endpoint không cần JWT, và check chạy TRƯỚC khi user login).
    /// </summary>
    public class UpdateChecker
    {
        private readonly string _serverBaseUrl;
        private readonly Version _currentVersion;
        private readonly HttpClient _http;

        public UpdateState State { get; private set; } = new UpdateState();

        public event EventHandler<UpdateState>? UpdateAvailable;

        public UpdateChecker(string serverBaseUrl, HttpClient? httpClient = null)
        {
            _serverBaseUrl = serverBaseUrl.TrimEnd('/');
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        /// <summary>
        /// Fire-and-forget check. Delay tránh chặn Revit startup.
        /// Failure (network error, parse error) → silent log + skip — không phá flow chính.
        /// </summary>
        public async Task CheckAsync(TimeSpan delay = default)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay).ConfigureAwait(false);
            try
            {
                var url = $"{_serverBaseUrl}/api/v1/version/check?current={_currentVersion}";
                var json = await _http.GetStringAsync(url).ConfigureAwait(false);
                var info = JsonConvert.DeserializeObject<VersionInfoDto>(json);
                if (info == null) return;

                var latest = ParseVersion(info.Latest);
                var minSupported = ParseVersion(info.MinSupported);

                State = new UpdateState
                {
                    CurrentVersion = _currentVersion,
                    LatestVersion = latest,
                    MinSupportedVersion = minSupported,
                    HasUpdate = latest > _currentVersion,
                    Mandatory = info.Mandatory || _currentVersion < minSupported,
                    DownloadUrl = info.DownloadUrl,
                    Sha256 = info.Sha256,
                    ReleaseNotes = info.ReleaseNotes,
                    Checked = true,
                };

                if (State.HasUpdate)
                {
                    UpdateAvailable?.Invoke(this, State);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MEPAuto] UpdateChecker fail: {ex.Message}");
                // Silent — version check fail không phá Revit startup.
            }
        }

        private static Version ParseVersion(string s)
        {
            if (string.IsNullOrEmpty(s)) return new Version(0, 0, 0);
            var clean = s.Split('-')[0];  // strip pre-release suffix
            return Version.TryParse(clean, out var v) ? v : new Version(0, 0, 0);
        }
    }

    public class UpdateState
    {
        public Version CurrentVersion { get; set; } = new Version(0, 0, 0);
        public Version LatestVersion { get; set; } = new Version(0, 0, 0);
        public Version MinSupportedVersion { get; set; } = new Version(0, 0, 0);
        public bool HasUpdate { get; set; }
        public bool Mandatory { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string ReleaseNotes { get; set; } = "";
        public bool Checked { get; set; }
    }
}
