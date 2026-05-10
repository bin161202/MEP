using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Revit;

namespace MEPAuto.Client.Common.Commands
{
    /// <summary>
    /// Base class cho mọi feature command. Lo:
    /// 1. Check IsOnline (heartbeat) — fail = thông báo + return Cancelled.
    /// 2. Check JWT cache hợp lệ — không có thì popup LoginDialog.
    /// 3. Build IFeatureContext (RevitSvc + ServerProxy + CurrentUser) → gọi <see cref="RunFeature"/>.
    /// 4. Catch <see cref="SessionExpiredException"/> → gợi ý re-login.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>[Transaction]</c> + <c>[Regeneration]</c> attribute KHÔNG inherit (Revit API design).
    /// Mỗi feature command kế thừa class này PHẢI khai báo lại 2 attribute trên class concrete:
    /// <code>
    /// [Transaction(TransactionMode.Manual)]
    /// [Regeneration(RegenerationOption.Manual)]
    /// public class MyFeatureCommand : BaseFeatureCommand { ... }
    /// </code>
    /// Quên = Revit báo "No Transaction Attribute" lúc click button.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public abstract class BaseFeatureCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var state = AuthBootstrap.Current;

            // Bước 1: kiểm tra session + online
            if (!AuthBootstrap.EnsureLoggedIn())
            {
                message = "Cần đăng nhập để dùng feature này.";
                return Result.Cancelled;
            }

            if (!state.Heartbeat.IsOnline)
            {
                TaskDialog.Show("MEPAuto", "Mất kết nối server. Vui lòng kiểm tra mạng và thử lại sau.");
                return Result.Cancelled;
            }

            // Bước 2: build context
            var user = state.CurrentUser ?? throw new InvalidOperationException("Token cache mất sau khi login?");
            var revitSvc = new RevitService(commandData.Application.ActiveUIDocument);
            var ctx = new FeatureContext(revitSvc, state.Server, user, commandData.Application);

            // Bước 3: chạy feature
            try
            {
                return RunFeature(ctx);
            }
            catch (SessionExpiredException)
            {
                state.Cache.Clear();
                TaskDialog.Show("MEPAuto", "Session đã hết hạn. Vui lòng click lại để đăng nhập.");
                return Result.Cancelled;
            }
            catch (ServerErrorException ex) when (ex.StatusCode == 403)
            {
                TaskDialog.Show("MEPAuto", "Bạn không có quyền dùng feature này. Liên hệ admin để được cấp license.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("MEPAuto - Lỗi", $"Feature gặp lỗi:\n\n{ex.Message}");
                return Result.Failed;
            }
        }

        /// <summary>Override trong class concrete feature command.</summary>
        protected abstract Result RunFeature(IFeatureContext ctx);
    }
}
