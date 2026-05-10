using System;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Contracts;
using MEPAuto.Client.Common.Events;
using MEPAuto.Client.Common.Revit;
using MEPAuto.Client.Common.Updater;
using MEPAuto.Client.Shell.Contracts;
using MEPAuto.Client.Shell.Ribbon;

namespace MEPAuto.Client.Shell
{
    /// <summary>
    /// Entry point của add-in MEPAuto. Revit gọi <see cref="OnStartup"/> 1 lần khi app khởi động.
    /// </summary>
    /// <remarks>
    /// Phase 1 (User mode): bootstrap auth state, scan IFeatureManifest qua reflection → build ribbon.
    /// Login KHÔNG popup tại startup (tránh chặn Revit khởi động) — sẽ popup khi user click feature đầu tiên.
    /// Heartbeat khởi động sau khi user login thành công.
    /// <para>
    /// Plan v3 (chuẩn bị 3 chế độ User/AI/CAD-PDF):
    /// - Scan IFeatureContract (headless entry) song song với IFeatureManifest (ribbon).
    /// - Tạo ExternalEvent + handler cho 2 use case:
    ///   • OfflineNoticeHandler: HeartbeatService raise khi mất mạng → modal tự nhảy.
    ///   • ServerStepHandler: JobPollerService (Phase C) raise khi server đẩy lệnh AI/CAD-PDF.
    /// - Cả 2 ExternalEvent.Create PHẢI gọi từ Revit context (OnStartup) — KHÔNG ở constructor service.
    /// </para>
    /// </remarks>
    public class RevitApp : IExternalApplication
    {
        /// <summary>Sổ đăng ký Contract — JobPoller (Phase C) + AI service dùng để resolve feature theo tên.</summary>
        public static IContractRegistry ContractRegistry { get; private set; } = null!;

        /// <summary>Trạng thái update từ VPS check lần gần nhất. Ribbon button có thể tô đỏ khi HasUpdate=true.</summary>
        public static UpdateChecker? Updater { get; private set; }

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                // 1. Auth state (HeartbeatService, ServerProxy, JwtCache)
                var state = AuthBootstrap.Initialize();

                // 2. Contract registry — quét reflection mọi DLL MEPAuto.*.dll tìm IFeatureContract
                ContractRegistry = new ContractRegistry();

                // 3. ExternalEvent — cây cầu luồng nền → Revit UI thread.
                //    Phải tạo trong OnStartup vì ExternalEvent.Create yêu cầu Revit API context.
                var offlineHandler = new OfflineNoticeHandler();
                var offlineEvent = ExternalEvent.Create(offlineHandler);
                OfflineNotifier.Bind(offlineHandler, offlineEvent);

                var stepHandler = new ServerStepHandler(
                    ContractRegistry,
                    BuildContextFactory(state));
                var stepEvent = ExternalEvent.Create(stepHandler);
                ServerStepDispatcher.Bind(stepHandler, stepEvent);

                // 4. Ribbon scan IFeatureManifest
                RibbonBuilder.Build(application);

                // 5. UpdateChecker — fire-and-forget. Delay 5s để không chặn Revit start.
                var updateHandler = new UpdatePromptHandler();
                var updateEvent = ExternalEvent.Create(updateHandler);
                UpdatePromptNotifier.Bind(updateHandler, updateEvent);

                Updater = new UpdateChecker(state.Config.ServerBaseUrl);
                Updater.UpdateAvailable += (s, st) =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MEPAuto] Update available: {st.CurrentVersion} → {st.LatestVersion} (mandatory={st.Mandatory})");
                    UpdatePromptNotifier.Raise(st);
                };
                _ = Updater.CheckAsync(TimeSpan.FromSeconds(5));

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MEPAuto - Startup Error", "Add-in MEPAuto khởi động thất bại:\n\n" + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            AuthBootstrap.Shutdown();
            return Result.Succeeded;
        }

        /// <summary>
        /// Factory build <see cref="IFeatureContext"/> cho ServerStepHandler — cần fresh UIDocument mỗi lần
        /// vì Revit có thể đã đổi document giữa các step.
        /// </summary>
        private static Func<UIApplication, IFeatureContext> BuildContextFactory(AuthState state)
        {
            return app =>
            {
                var user = state.CurrentUser ?? throw new InvalidOperationException(
                    "ServerStepHandler không có CurrentUser. User chưa login → AI mode không thể chạy.");
                var revitSvc = new RevitService(app.ActiveUIDocument);
                return new FeatureContext(revitSvc, state.Server, user, app);
            };
        }
    }
}
