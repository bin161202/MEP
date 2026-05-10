using Autodesk.Revit.UI;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// Static facade — luồng nền (JobPollerService Phase C tương lai) gọi <see cref="Dispatch"/> để đẩy step
    /// xuống client mà không phải biết về <see cref="ExternalEvent"/> hay <see cref="ServerStepHandler"/> instance.
    /// Shell wire vào lúc OnStartup qua <see cref="Bind"/>.
    /// </summary>
    public static class ServerStepDispatcher
    {
        private static ServerStepHandler? _handler;
        private static ExternalEvent? _event;
        private static readonly object _lock = new object();

        /// <summary>Shell gọi 1 lần ở RevitApp.OnStartup sau khi <see cref="ExternalEvent.Create"/>.</summary>
        public static void Bind(ServerStepHandler handler, ExternalEvent evt)
        {
            lock (_lock)
            {
                _handler = handler;
                _event = evt;
            }
        }

        /// <summary>
        /// Luồng nền call để đẩy step. No-op nếu chưa Bind (Revit chưa khởi động xong).
        /// Caller chịu trách nhiệm set <c>req.OnComplete</c> để nhận kết quả async.
        /// </summary>
        public static bool Dispatch(StepRequest req)
        {
            ServerStepHandler? handler;
            ExternalEvent? evt;
            lock (_lock)
            {
                handler = _handler;
                evt = _event;
            }
            if (handler == null || evt == null) return false;
            handler.Enqueue(req);
            evt.Raise();
            return true;
        }
    }
}
