using System;
using MEPAuto.Client.Common.Auth;

namespace MEPAuto.Client.Common.Contracts
{
    /// <summary>
    /// Khuôn mẫu cho mọi feature MEPAuto — entry point HEADLESS (không cần ribbon click).
    /// User mode (IExternalCommand) gọi <see cref="Execute"/> sau khi build input từ UI.
    /// AI/CAD-PDF mode (luồng nền) cũng gọi <see cref="Execute"/> với input DTO server đẩy xuống.
    /// → Cùng 1 method, dùng được 3 mode.
    /// </summary>
    /// <remarks>
    /// Quy tắc:
    /// - Implementation KHÔNG show <c>TaskDialog</c> / WPF dialog (luồng nền không show được).
    /// - Implementation KHÔNG gọi <c>PickObject</c> (luồng nền không có user pick).
    /// - Implementation CÓ THỂ gọi <c>IRevitService</c> + <c>IServerProxy</c>.
    /// </remarks>
    public interface IFeatureContract
    {
        /// <summary>Tên feature, khớp với <c>IFeatureManifest.Name</c> (vd "HelloWorld").</summary>
        string FeatureName { get; }

        /// <summary>Loại DTO mà <see cref="Execute"/> nhận (vd <c>typeof(HelloWorldRequest)</c>).
        /// Server đẩy lệnh xuống dạng JSON → registry deserialize sang đúng type này.</summary>
        Type InputType { get; }

        /// <summary>
        /// Chạy logic feature. Gọi được từ cả luồng UI Revit (User mode) lẫn luồng nền sau ExternalEvent.
        /// </summary>
        /// <param name="ctx">Feature context với RevitSvc + Server + CurrentUser.</param>
        /// <param name="input">DTO input — phải khớp <see cref="InputType"/>.</param>
        /// <returns>Output DTO của feature (vd <c>HelloWorldResponse</c>).</returns>
        object Execute(IFeatureContext ctx, object input);
    }
}
