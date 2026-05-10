using System;
using System.IO;
using System.Reflection;
using MEPAuto.Server.Api.Middleware;
using MEPAuto.Server.Core.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace MEPAuto.Server.Api.Controllers
{
    /// <summary>
    /// Liveness/readiness check cho nginx + monitoring. KHÔNG yêu cầu auth.
    /// Trả 503 nếu critical check (data dir writable, JWT key) fail → nginx có thể routing fail traffic.
    /// </summary>
    [ApiController, Route("health"), AllowAnonymous]
    public class HealthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IOptions<JwtOptions> _jwt;
        private readonly MetricsCollector _metrics;

        public HealthController(IConfiguration config, IOptions<JwtOptions> jwt, MetricsCollector metrics)
        {
            _config = config;
            _jwt = jwt;
            _metrics = metrics;
        }

        [HttpGet]
        public IActionResult Get()
        {
            var dataDir = _config["DataDir"] ?? "/var/mepauto-data";
            var (dataDirOk, dataDirError) = CheckDataDirWritable(dataDir);
            var jwtOk = !string.IsNullOrEmpty(_jwt.Value.SigningKey) && _jwt.Value.SigningKey.Length >= 32;

            var allOk = dataDirOk && jwtOk;
            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

            var body = new
            {
                status = allOk ? "ok" : "degraded",
                version = assemblyVersion,
                time = DateTime.UtcNow,
                uptimeSeconds = _metrics.Uptime.TotalSeconds,
                checks = new
                {
                    dataDirWritable = new { ok = dataDirOk, path = dataDir, error = dataDirError },
                    jwtConfigured = new { ok = jwtOk, keyLengthBytes = _jwt.Value.SigningKey?.Length ?? 0 },
                }
            };

            return allOk ? Ok(body) : StatusCode(503, body);
        }

        private static (bool ok, string? error) CheckDataDirWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return (false, "directory không tồn tại");
                var probe = Path.Combine(dir, $".health-{Guid.NewGuid():N}.tmp");
                System.IO.File.WriteAllText(probe, "ok");
                System.IO.File.Delete(probe);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
