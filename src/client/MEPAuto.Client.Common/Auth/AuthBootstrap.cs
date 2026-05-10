using System;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// Singleton service container cho Client-side: ClientConfig, JwtCache, ServerProxy, HeartbeatService.
    /// RevitApp.OnStartup gọi <see cref="Initialize"/> 1 lần. Feature command gọi <see cref="Current"/> để dùng.
    /// </summary>
    public static class AuthBootstrap
    {
        private static AuthState? _state;

        public static AuthState Current => _state ?? throw new InvalidOperationException(
            "AuthBootstrap.Initialize() chưa được gọi. Hãy gọi trong RevitApp.OnStartup.");

        public static AuthState Initialize()
        {
            if (_state != null) return _state;
            var config = ClientConfig.Load();
            var cache = new JwtCache();
            var server = new ServerProxy(config.ServerBaseUrl, cache);
            var heartbeat = new HeartbeatService(server);
            _state = new AuthState(config, cache, server, heartbeat);
            return _state;
        }

        public static void Shutdown()
        {
            _state?.Heartbeat.Dispose();
            _state = null;
        }

        /// <summary>
        /// Hiện LoginDialog, lưu token nếu thành công. Trả true nếu user đã đăng nhập (cache hợp lệ hoặc login mới).
        /// Gọi từ UI thread.
        /// </summary>
        public static bool EnsureLoggedIn()
        {
            var state = _state ?? Initialize();
            var existing = state.Cache.Load();
            if (existing != null && !string.IsNullOrEmpty(existing.AccessToken) && existing.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
            {
                state.Heartbeat.Start();
                return true;
            }
            var dialog = new LoginDialog(state.Server, state.Cache);
            var result = dialog.ShowDialog();
            if (result == true && dialog.LoggedIn)
            {
                state.Heartbeat.Start();
                return true;
            }
            return false;
        }
    }

    public class AuthState
    {
        public ClientConfig Config { get; }
        public JwtCache Cache { get; }
        public IServerProxy Server { get; }
        public HeartbeatService Heartbeat { get; }

        public AuthState(ClientConfig config, JwtCache cache, IServerProxy server, HeartbeatService heartbeat)
        {
            Config = config;
            Cache = cache;
            Server = server;
            Heartbeat = heartbeat;
        }

        public CurrentUserInfo? CurrentUser
        {
            get
            {
                var token = Cache.Load();
                if (token == null) return null;
                return new CurrentUserInfo { UserId = token.UserId, Email = token.Email };
            }
        }
    }
}
