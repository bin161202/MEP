using System;
using System.Threading;
using System.Threading.Tasks;
using MEPAuto.Client.Common.Events;
using MEPAuto.Contracts.Auth;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// Background timer gọi <c>/api/v1/auth/heartbeat</c> mỗi 30s.
    /// Sau 3 fail liên tiếp (90s) → <see cref="IsOnline"/> = false, raise <see cref="OnlineStateChanged"/>.
    /// Ribbon command kiểm tra <see cref="IsOnline"/> trước khi cho phép thực thi feature.
    /// </summary>
    public class HeartbeatService : IDisposable
    {
        public const int HeartbeatIntervalSec = 30;
        public const int FailThreshold = 3;

        private readonly IServerProxy _server;
        private Timer? _timer;
        private int _consecutiveFailures;
        private bool _isOnline = true;
        private readonly object _lock = new object();

        public bool IsOnline
        {
            get { lock (_lock) return _isOnline; }
        }

        public event EventHandler<bool>? OnlineStateChanged;

        public HeartbeatService(IServerProxy server) { _server = server; }

        public void Start()
        {
            _timer ??= new Timer(async _ => await Tick().ConfigureAwait(false),
                state: null,
                dueTime: TimeSpan.FromSeconds(5),
                period: TimeSpan.FromSeconds(HeartbeatIntervalSec));
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();

        private async Task Tick()
        {
            try
            {
                var resp = await _server.Get<HeartbeatResponse>("/api/v1/auth/heartbeat").ConfigureAwait(false);
                if (resp.Status == "ok")
                {
                    _consecutiveFailures = 0;
                    SetOnline(true);
                    return;
                }
                RecordFailure();
            }
            catch (SessionExpiredException)
            {
                RecordFailure(forceOffline: true);
            }
            catch
            {
                RecordFailure();
            }
        }

        private void RecordFailure(bool forceOffline = false)
        {
            _consecutiveFailures++;
            if (forceOffline || _consecutiveFailures >= FailThreshold)
            {
                SetOnline(false);
            }
        }

        private void SetOnline(bool online)
        {
            bool changed;
            lock (_lock)
            {
                changed = _isOnline != online;
                _isOnline = online;
            }
            if (changed)
            {
                if (!online)
                {
                    // Mất mạng → raise ExternalEvent (proactive notification, không chờ user click feature).
                    // Dedupe: chỉ raise lúc CHUYỂN từ online → offline, không raise mỗi tick fail.
                    // No-op nếu Shell chưa Bind (vd test, hoặc Revit chưa OnStartup xong).
                    OfflineNotifier.Raise("Mất kết nối server MEPAuto. Vui lòng kiểm tra mạng và thử lại sau.");
                }
                OnlineStateChanged?.Invoke(this, online);
            }
        }
    }
}
