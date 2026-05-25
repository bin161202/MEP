using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Commands;
using MEPAuto.Contracts.DTOs;
using MEPAuto.SheetFromExcel.Views;

namespace MEPAuto.SheetFromExcel.Commands
{
    /// <summary>
    /// Feature command: tạo Sheet/View/Viewport từ file Excel.
    /// Logic gốc giữ nguyên trong <see cref="SheetFromExcelWindow"/> — command chỉ wrap
    /// auth/license flow theo chuẩn MEPAuto rồi mở dialog.
    /// </summary>
    /// <remarks>
    /// PickPoint bắt buộc user tương tác → headless mode chỉ hỗ trợ license check + audit.
    /// </remarks>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class SheetFromExcelCommand : BaseFeatureCommand
    {
        protected override Result RunFeature(IFeatureContext ctx)
        {
            // 1. License check + audit via server
            var input = BuildInput(ctx);
            var output = ExecuteHeadless(ctx, input);

            // 2. Show dialog — toàn bộ logic gốc nằm trong Window code-behind
            var uidoc = ctx.UiApp.ActiveUIDocument;
            var window = new SheetFromExcelWindow(uidoc.Document);
            window.ShowDialog();

            // 3. Report result
            ctx.Server.Post(
                "/api/v1/sheetfromexcel/result",
                new SheetFromExcelResultRequest
                {
                    JobId = output.JobId,
                    Success = true,
                }).GetAwaiter().GetResult();

            return Result.Succeeded;
        }

        /// <summary>
        /// Headless entry: POST server để license check + audit.
        /// Logic Revit nặng (PickPoint, Transaction) nằm trong Window — không gọi được headless.
        /// </summary>
        public static SheetFromExcelResponse ExecuteHeadless(IFeatureContext ctx, SheetFromExcelRequest input)
        {
            var output = ctx.Server.Post<SheetFromExcelResponse>(
                "/api/v1/sheetfromexcel/execute", input).GetAwaiter().GetResult();
            return output;
        }

        private static SheetFromExcelRequest BuildInput(IFeatureContext ctx)
        {
            return new SheetFromExcelRequest
            {
                Snapshot = new SheetFromExcelSnapshotData
                {
                    UserEmail = ctx.CurrentUser.Email,
                },
            };
        }
    }
}
