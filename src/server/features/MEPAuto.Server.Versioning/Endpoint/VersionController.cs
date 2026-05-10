using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using MEPAuto.Server.Versioning.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MEPAuto.Server.Versioning.Endpoint
{
    /// <summary>
    /// Endpoint check version. Anonymous — version check phải work cả lúc user chưa login.
    /// </summary>
    [ApiController]
    [Route("api/v1/version")]
    [AllowAnonymous]
    public class VersionController : ControllerBase
    {
        private readonly VersionService _svc;
        public VersionController(VersionService svc) { _svc = svc; }

        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] string? current)
        {
            var info = await _svc.GetVersionInfo(current ?? "");
            return Ok(info);
        }
    }
}
