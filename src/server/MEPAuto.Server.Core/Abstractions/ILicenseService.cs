using System.Security.Claims;
using System.Threading.Tasks;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// Check user có quyền dùng feature không. Phase 1 = JSON file; Phase 2 = DB query.
    /// Đọc claim "licenses" từ JWT; nếu empty → fallback query repository.
    /// </summary>
    public interface ILicenseService
    {
        Task<bool> CanUse(ClaimsPrincipal user, string feature);
        Task<string[]> GetFeatures(string email);
    }
}
