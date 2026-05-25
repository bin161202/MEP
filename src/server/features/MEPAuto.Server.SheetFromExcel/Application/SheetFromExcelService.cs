using System.Security.Claims;
using System.Threading.Tasks;
using MEPAuto.Contracts.DTOs;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.SheetFromExcel.Domain;

namespace MEPAuto.Server.SheetFromExcel.Application
{
    public class SheetFromExcelService
    {
        private readonly IAuditLogger _audit;
        public SheetFromExcelService(IAuditLogger audit) { _audit = audit; }

        public async Task<SheetFromExcelResponse> Execute(SheetFromExcelRequest req, ClaimsPrincipal user)
        {
            var jobId = SheetFromExcelLogic.BuildJobId();
            await _audit.Log(user, "sheetfromexcel.execute", new { jobId, req.Snapshot.UserEmail, req.Snapshot.SheetCount });
            return new SheetFromExcelResponse { JobId = jobId };
        }

        public async Task RecordResult(SheetFromExcelResultRequest req, ClaimsPrincipal user)
        {
            await _audit.Log(user, "sheetfromexcel.result",
                new { req.JobId, req.Success, req.SheetsCreated, req.ViewportsMoved },
                req.Success ? "ok" : "fail");
        }
    }
}
