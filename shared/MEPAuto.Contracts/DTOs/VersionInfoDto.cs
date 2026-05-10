using System.Collections.Generic;

namespace MEPAuto.Contracts.DTOs
{
    /// <summary>
    /// Response của GET /api/v1/version/check. Client gọi lúc startup, so sánh với assembly version.
    /// Endpoint anonymous (không cần JWT) — version check phải work cả khi user chưa login.
    /// </summary>
    public class VersionInfoDto
    {
        /// <summary>SemVer cao nhất đang có MSI (vd "0.1.5").</summary>
        public string Latest { get; set; } = "";

        /// <summary>Version tối thiểu vẫn hỗ trợ. Client &lt; ngưỡng này sẽ bị block (Mandatory).</summary>
        public string MinSupported { get; set; } = "";

        /// <summary>URL trực tiếp tới MSI (GitHub Release asset). Có thể có placeholder {version}.</summary>
        public string DownloadUrl { get; set; } = "";

        /// <summary>SHA256 hex lowercase của MSI. Client verify trước khi chạy installer.</summary>
        public string Sha256 { get; set; } = "";

        /// <summary>True khi current &lt; MinSupported → tất cả command bị block.</summary>
        public bool Mandatory { get; set; }

        /// <summary>Markdown changelog từ phiên bản user hiện tại tới Latest. Hiển thị trong UpdatePromptWindow.</summary>
        public string ReleaseNotes { get; set; } = "";

        /// <summary>List version hỗ trợ Revit cho Latest (vd ["2024", "2025"]).</summary>
        public List<string> RevitVersions { get; set; } = new List<string>();
    }
}
