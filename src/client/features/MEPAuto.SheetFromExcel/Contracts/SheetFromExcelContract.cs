using System;
using MEPAuto.Client.Common.Auth;
using MEPAuto.Client.Common.Contracts;
using MEPAuto.Contracts.DTOs;
using MEPAuto.SheetFromExcel.Commands;

namespace MEPAuto.SheetFromExcel.Contracts
{
    /// <summary>
    /// Contract HEADLESS cho feature SheetFromExcel.
    /// Feature này yêu cầu PickPoint (user chọn 2 điểm trên sheet) nên headless mode
    /// chỉ hỗ trợ license check + audit — không thực thi Revit operations.
    /// </summary>
    public class SheetFromExcelContract : IFeatureContract
    {
        public string FeatureName => "SheetFromExcel";
        public Type InputType => typeof(SheetFromExcelRequest);

        public object Execute(IFeatureContext ctx, object input)
        {
            if (input is not SheetFromExcelRequest req)
                throw new ArgumentException(
                    $"SheetFromExcelContract.Execute expected SheetFromExcelRequest, got {input?.GetType().Name ?? "null"}",
                    nameof(input));

            return SheetFromExcelCommand.ExecuteHeadless(ctx, req);
        }
    }
}
