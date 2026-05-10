using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.HelloWorld.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MEPAuto.Server.HelloWorld.Endpoint
{
    [ApiController]
    [Route("api/v1/helloworld")]
    [Authorize]
    public class HelloWorldController : ControllerBase
    {
        private const string LicenseFeature = "helloworld.basic";

        private readonly HelloWorldService _svc;
        private readonly ILicenseService _license;

        public HelloWorldController(HelloWorldService svc, ILicenseService license)
        {
            _svc = svc;
            _license = license;
        }

        [HttpPost("execute")]
        public async Task<IActionResult> Execute([FromBody] HelloWorldRequest req)
        {
            if (!await _license.CanUse(User, LicenseFeature))
                return StatusCode(403, new { error = "license_required", feature = LicenseFeature });

            var resp = await _svc.Execute(req, User);
            return Ok(resp);
        }

        [HttpPost("result")]
        public async Task<IActionResult> Result([FromBody] HelloWorldResultRequest req)
        {
            if (!await _license.CanUse(User, LicenseFeature))
                return StatusCode(403, new { error = "license_required", feature = LicenseFeature });

            await _svc.RecordResult(req, User);
            return Ok();
        }
    }
}
