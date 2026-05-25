using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.SheetFromExcel.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MEPAuto.Server.SheetFromExcel.Endpoint
{
    [ApiController]
    [Route("api/v1/sheetfromexcel")]
    [Authorize]
    public class SheetFromExcelController : ControllerBase
    {
        private const string LicenseFeature = "sheetfromexcel.basic";

        private readonly SheetFromExcelService _svc;
        private readonly ILicenseService _license;

        public SheetFromExcelController(SheetFromExcelService svc, ILicenseService license)
        {
            _svc = svc;
            _license = license;
        }

        [HttpPost("execute")]
        public async Task<IActionResult> Execute([FromBody] SheetFromExcelRequest req)
        {
            if (!await _license.CanUse(User, LicenseFeature))
                return StatusCode(403, new { error = "license_required", feature = LicenseFeature });

            var resp = await _svc.Execute(req, User);
            return Ok(resp);
        }

        [HttpPost("result")]
        public async Task<IActionResult> Result([FromBody] SheetFromExcelResultRequest req)
        {
            if (!await _license.CanUse(User, LicenseFeature))
                return StatusCode(403, new { error = "license_required", feature = LicenseFeature });

            await _svc.RecordResult(req, User);
            return Ok();
        }
    }
}
