using Autodesk.Revit.UI;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// Static facade — luồng nền (HeartbeatService) gọi <see cref="Raise"/> để show offline modal trên UI thread.
    /// Shell (RevitApp.OnStartup) gọi <see cref="Bind"/> 1 lần với handler + ExternalEvent đã tạo trong Revit context.
    /// </summary>
    /// <remarks>
    /// Tách static facade khỏi <see cref="OfflineNoticeHandler"/> để:
    /// - Common không phải reference Shell (cycle ref).
    /// - HeartbeatService không phải biết ExternalEvent — chỉ gọi <c>OfflineNotifier.Raise(msg)</c>.
    /// - Khi chưa Bind (vd unit test) → no-op, không crash.
    /// </remarks>
    public static class OfflineNotifier
    {
        private static OfflineNoticeHandler? _handler;
        private static ExternalEvent? _event;
        private static readonly object _lock = new object();

        /// <summary>Shell gọi 1 lần ở RevitApp.OnStartup sau khi <see cref="ExternalEvent.Create"/>.</summary>
        public static void Bind(OfflineNoticeHandler handler, ExternalEvent evt)
        {
            lock (_lock)
            {
                _handler = handler;
                _event = evt;
            }
        }

        /// <summary>
        /// Luồng nền gọi để báo offline. Raise sẽ no-op nếu Bind chưa được gọi (vd test, hoặc Revit chưa OnStartup xong).
        /// Set message + raise — Revit sẽ pop TaskDialog ở idle frame tiếp theo.
        /// </summary>
        public static void Raise(string message)
        {
            OfflineNoticeHandler? handler;
            ExternalEvent? evt;
            lock (_lock)
            {
                handler = _handler;
                evt = _event;
            }
            if (handler == null || evt == null) return;
            handler.Message = message;
            evt.Raise();
        }
    }
}
