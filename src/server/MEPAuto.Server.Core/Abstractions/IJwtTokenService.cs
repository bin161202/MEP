using System;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Models;

namespace MEPAuto.Server.Core.Abstractions
{
    /// <summary>
    /// Sinh + validate JWT access token + refresh token.
    /// Implementation ở Server.Api (vì cần JwtOptions từ DI).
    /// </summary>
    public interface IJwtTokenService
    {
        /// <summary>Sinh access token cho user, embed claims sub/email/licenses.</summary>
        string CreateAccessToken(User user, string[] features, out DateTime expiresAt);

        /// <summary>Sinh refresh token (random opaque), persist qua IDataStorageService.</summary>
        Task<string> CreateRefreshToken(string userId);

        /// <summary>Validate refresh token, trả userId nếu hợp lệ; null nếu invalid/revoked/expired.</summary>
        Task<string?> ValidateRefreshToken(string refreshToken);

        /// <summary>Revoke refresh token (logout).</summary>
        Task RevokeRefreshToken(string refreshToken);
    }
}
