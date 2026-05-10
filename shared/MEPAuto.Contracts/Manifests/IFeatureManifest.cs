using System;

namespace MEPAuto.Contracts.Manifests
{
    /// <summary>
    /// Mọi feature trong MEPAuto đều implement interface này.
    /// Client.Shell scan reflection toàn bộ assembly feature → tạo button ribbon từ Manifest.
    /// </summary>
    public interface IFeatureManifest
    {
        /// <summary>Tên technical, dùng làm key (không space).</summary>
        string Name { get; }

        /// <summary>Tên hiển thị trên button ribbon.</summary>
        string DisplayName { get; }

        /// <summary>Endpoint server-side, vd "/api/v1/sprinkler/execute".</summary>
        string ServerEndpoint { get; }

        /// <summary>Tên license feature, vd "sprinkler.basic". Server check user có quyền dùng.</summary>
        string LicenseFeature { get; }

        /// <summary>Tên panel ribbon, vd "MEP - Electrical". Cùng PanelGroup → cùng panel.</summary>
        string PanelGroup { get; }

        /// <summary>Thứ tự button trong panel (asc).</summary>
        int Order { get; }

        /// <summary>Đường dẫn tới icon embedded resource, vd "Icons/sprinkler.png".</summary>
        string IconResourcePath { get; }

        /// <summary>Type của IExternalCommand thực thi feature.</summary>
        Type CommandType { get; }
    }
}
