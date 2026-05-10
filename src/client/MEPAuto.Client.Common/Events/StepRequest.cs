using System;

namespace MEPAuto.Client.Common.Events
{
    /// <summary>
    /// 1 lệnh server đẩy xuống client để execute headless (AI/CAD-PDF mode).
    /// JobPollerService nhận từ <c>/api/v1/jobs/{id}/next-step</c> → enqueue vào <c>ServerStepHandler</c>
    /// → ExternalEvent dispatch sang UI thread → resolve Contract qua FeatureName → execute.
    /// </summary>
    public class StepRequest
    {
        /// <summary>Job ID server tạo, dùng để báo cáo result về.</summary>
        public string JobId { get; set; } = "";

        /// <summary>Tên feature cần chạy, khớp <c>IFeatureContract.FeatureName</c> (vd "HelloWorld").</summary>
        public string FeatureName { get; set; } = "";

        /// <summary>Input DTO dạng JSON. Handler deserialize sang đúng <c>IFeatureContract.InputType</c>.</summary>
        public string InputJson { get; set; } = "";

        /// <summary>Callback sau khi execute xong (success hoặc fail). Caller (JobPoller) dùng để POST result về server.
        /// <c>output</c> là kết quả Contract.Execute (hoặc null nếu lỗi); <c>error</c> là exception (hoặc null nếu OK).</summary>
        public Action<object?, Exception?>? OnComplete { get; set; }
    }
}
