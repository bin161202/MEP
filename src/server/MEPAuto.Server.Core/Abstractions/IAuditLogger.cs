using System.Security.Claims;
using System.Threading.Tasks;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// Append audit entry cho mọi action có auth. Phase 1 = JSON-line vào /var/mepauto-data/audit.log;
    /// Phase 2 = structured log → ELK / centralised logging.
    /// KHÔNG bao giờ ghi password/token raw.
    /// </summary>
    public interface IAuditLogger
    {
        Task Log(ClaimsPrincipal? user, string action, object? data = null, string status = "ok", int durationMs = 0);
    }
}
