using MEPAuto.Server.Api.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MEPAuto.Server.Api.Controllers
{
    /// <summary>
    /// Trả snapshot in-memory metrics: counter total/error, latency p50/p95/p99 rolling 5 min, breakdown theo endpoint.
    /// Yêu cầu auth (JWT) để tránh leak thông tin endpoint cho external.
    /// </summary>
    [ApiController, Route("metrics"), Authorize]
    public class MetricsController : ControllerBase
    {
        private readonly MetricsCollector _collector;
        public MetricsController(MetricsCollector collector) { _collector = collector; }

        [HttpGet]
        public IActionResult Get() => Ok(_collector.Snapshot());
    }
}
