using Autodesk.Revit.UI;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// ExternalEvent handler hiện modal "Mất kết nối server" khi heartbeat fail 3 lần liên tiếp.
    /// Raise từ luồng Timer (HeartbeatService) — Revit dispatch sang UI thread idle frame tiếp theo
    /// → <see cref="Execute"/> chạy ở Revit API context an toàn.
    /// </summary>
    /// <remarks>
    /// Phải gọi <see cref="ExternalEvent.Create"/> trong Revit API context (RevitApp.OnStartup),
    /// KHÔNG trong constructor service — sẽ throw "Unable to execute Revit API outside of Revit API context".
    /// Wiring với <see cref="OfflineNotifier"/> ở Shell để HeartbeatService raise được không vướng cycle ref.
    /// </remarks>
    public class OfflineNoticeHandler : IExternalEventHandler
    {
        /// <summary>Set từ luồng nền TRƯỚC khi <see cref="ExternalEvent.Raise"/>.
        /// Revit đọc ở UI thread → race chỉ xảy ra nếu raise nhiều lần liên tiếp; OfflineNotifier dedupe.</summary>
        public string Message { get; set; } = "";

        public void Execute(UIApplication app)
        {
            TaskDialog.Show("MEPAuto", Message);
            // TODO Phase 2: grey-out ribbon panels qua RibbonHelper.SetEnabled(false)
        }

        public string GetName() => "MEPAuto.OfflineNotice";
    }
}
