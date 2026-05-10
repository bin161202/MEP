using Autodesk.Revit.UI;
using MEPAuto.Client.Common.Revit;

namespace MEPAuto.Client.Common.Auth
{
    /// <summary>
    /// Context truyền vào mỗi feature command. BaseFeatureCommand build sẵn, feature chỉ dùng.
    /// </summary>
    public interface IFeatureContext
    {
        /// <summary>Wrap Revit API — probe + apply qua interface, KHÔNG gọi Revit API trực tiếp.</summary>
        IRevitService RevitSvc { get; }

        /// <summary>HTTP proxy gọi VPS, đã inject Bearer token.</summary>
        IServerProxy Server { get; }

        /// <summary>Info user đang đăng nhập (từ JWT cache).</summary>
        CurrentUserInfo CurrentUser { get; }

        /// <summary>UIApplication của Revit — feature dùng để hiện dialog/get UI document.</summary>
        UIApplication UiApp { get; }
    }

    public class CurrentUserInfo
    {
        public string UserId { get; set; } = "";
        public string Email { get; set; } = "";
    }
}
