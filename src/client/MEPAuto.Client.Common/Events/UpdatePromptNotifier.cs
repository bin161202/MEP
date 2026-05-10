using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Updater;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// Static facade — UpdateChecker (background task) gọi <see cref="Raise"/> khi có update.
    /// Shell bind handler + ExternalEvent ở RevitApp.OnStartup.
    ///
    /// Dedupe: chỉ raise 1 lần / startup, không raise mỗi lần check (UpdateChecker check 1 lần lúc startup).
    /// </summary>
    public static class UpdatePromptNotifier
    {
        private static UpdatePromptHandler? _handler;
        private static ExternalEvent? _event;
        private static readonly object _lock = new object();
        private static bool _raisedOnce;

        public static void Bind(UpdatePromptHandler handler, ExternalEvent evt)
        {
            lock (_lock)
            {
                _handler = handler;
                _event = evt;
            }
        }

        public static void Raise(UpdateState state)
        {
            UpdatePromptHandler? handler;
            ExternalEvent? evt;
            lock (_lock)
            {
                if (_raisedOnce) return;  // dedupe
                _raisedOnce = true;
                handler = _handler;
                evt = _event;
            }
            if (handler == null || evt == null) return;
            handler.PendingState = state;
            evt.Raise();
        }
    }
}
