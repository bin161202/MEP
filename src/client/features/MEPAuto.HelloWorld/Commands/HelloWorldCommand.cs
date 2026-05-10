using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Commands;
using MEPAuto.Contracts.DTOs;

namespace MEPAuto.HelloWorld.Commands
{
    /// <summary>
    /// Pilot feature: ribbon click → POST server → server return greeting → TaskDialog hiện kết quả.
    /// Verify end-to-end: ribbon scan → JWT inject → license check → audit → response wire.
    /// </summary>
    /// <remarks>
    /// Cấu trúc 3 method tách bạch (Plan v3 — chuẩn bị 3 chế độ User/AI/CAD-PDF):
    ///   - <see cref="RunFeature"/>: cửa cho USER bấm ribbon.
    ///   - <see cref="ExecuteHeadless"/>: cửa cho LUỒNG NỀN (AI/CAD-PDF) gọi qua <c>HelloWorldContract</c>.
    ///   - <see cref="BuildInput"/> / <see cref="ShowResult"/>: helper riêng User mode.
    ///
    /// LƯU Ý: <c>[Transaction]</c> KHÔNG inherit từ <see cref="BaseFeatureCommand"/> — mỗi feature command
    /// PHẢI khai báo lại attribute này trực tiếp trên class concrete. Revit API design là vậy.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HelloWorldCommand : BaseFeatureCommand
    {
        // ========== Cửa cho USER bấm ribbon ==========
        protected override Result RunFeature(IFeatureContext ctx)
        {
            var input = BuildInput(ctx);
            var output = ExecuteHeadless(ctx, input);
            ShowResult(output);
            return Result.Succeeded;
        }

        // ========== Cửa HEADLESS — gọi được từ luồng nền (AI/CAD-PDF mode) ==========
        /// <summary>
        /// Logic CHÍNH: POST execute → POST result. KHÔNG show dialog.
        /// Sync wrap (Revit IExternalCommand không async-friendly — ServerProxy.Post async,
        /// GetAwaiter().GetResult() OK trong scope command vì ServerProxy đã ConfigureAwait(false) bên trong).
        /// </summary>
        public static HelloWorldResponse ExecuteHeadless(IFeatureContext ctx, HelloWorldRequest input)
        {
            var output = ctx.Server.Post<HelloWorldResponse>(
                "/api/v1/helloworld/execute", input).GetAwaiter().GetResult();

            ctx.Server.Post(
                "/api/v1/helloworld/result",
                new HelloWorldResultRequest { JobId = output.JobId, Success = true })
                .GetAwaiter().GetResult();

            return output;
        }

        // ========== Helper riêng cho User mode ==========
        private static HelloWorldRequest BuildInput(IFeatureContext ctx)
        {
            return new HelloWorldRequest
            {
                Snapshot = new HelloWorldSnapshotData { UserName = ctx.CurrentUser.Email },
            };
        }

        private static void ShowResult(HelloWorldResponse output)
        {
            TaskDialog.Show("MEPAuto - Hello World", output.Message);
        }
    }
}
