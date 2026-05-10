using System;
using System.Threading.Tasks;
using System.Windows;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Updater;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// ExternalEvent handler hiện <see cref="UpdatePromptWindow"/> trên UI thread Revit.
    /// UpdateChecker (background task) raise qua <see cref="UpdatePromptNotifier"/>.
    ///
    /// Nếu user chọn "Cập nhật ngay": download MSI + verify SHA256 + schedule install.
    /// Nếu user chọn "Để sau"/"Bỏ qua": không làm gì (lần startup kế sẽ check lại; "Bỏ qua" sẽ wire
    /// SkippedVersions ở M3 — Phase này chấp nhận hỏi mỗi startup nếu chưa cài).
    /// </summary>
    public class UpdatePromptHandler : IExternalEventHandler
    {
        private readonly UpdateInstaller _installer;
        public UpdateState? PendingState { get; set; }

        public UpdatePromptHandler(UpdateInstaller? installer = null)
        {
            _installer = installer ?? new UpdateInstaller();
        }

        public void Execute(UIApplication app)
        {
            var state = PendingState;
            if (state == null) return;
            PendingState = null;  // consume

            try
            {
                var dialog = new UpdatePromptWindow(state);
                // Set Owner = Revit main window để dialog modal đúng (không bị Revit window che).
                try
                {
                    var hwnd = app.MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        new System.Windows.Interop.WindowInteropHelper(dialog).Owner = hwnd;
                    }
                }
                catch { /* MainWindowHandle không có ở 1 số version Revit cũ — fallback dialog không owner */ }

                dialog.ShowDialog();

                if (dialog.Result == UpdatePromptWindow.Choice.UpdateNow)
                {
                    // Fire-and-forget download + schedule. Show busy modal để user biết đang download.
                    _ = HandleUpdateNow(state, app);
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MEPAuto - Update Error",
                    $"Lỗi hiện cửa sổ cập nhật: {ex.Message}\n\nVui lòng tải MSI thủ công từ {state.DownloadUrl}");
            }
        }

        private async Task HandleUpdateNow(UpdateState state, UIApplication app)
        {
            try
            {
                var msiPath = await _installer.DownloadAsync(state.DownloadUrl, state.Sha256).ConfigureAwait(false);
                _installer.ScheduleInstall(msiPath);

                // Nhảy về UI thread show TaskDialog hướng dẫn đóng Revit.
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    TaskDialog.Show("MEPAuto - Sẵn sàng cập nhật",
                        "MSI đã download và xác minh thành công.\n\n" +
                        "Vui lòng SAVE file đang mở rồi ĐÓNG Revit. " +
                        "Trình cài đặt sẽ tự chạy sau khi Revit thoát.");
                });
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    TaskDialog.Show("MEPAuto - Update Failed",
                        $"Không tải được bản cập nhật:\n\n{ex.Message}\n\n" +
                        $"Vui lòng tải MSI thủ công từ:\n{state.DownloadUrl}");
                });
            }
        }

        public string GetName() => "MEPAuto.UpdatePrompt";
    }
}
