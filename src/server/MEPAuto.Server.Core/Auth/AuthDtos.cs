using System;

namespace MEPAuto.Server.Core.Auth
{
    /// <summary>
    /// Server-only auth state. Wire DTOs (LoginRequest/Response/RefreshRequest/HeartbeatResponse)
    /// nằm ở <c>MEPAuto.Contracts.Auth</c> vì Client cũng cần.
    /// </summary>
    public class RefreshTokenState
    {
        public string Token { get; set; } = "";
        public string UserId { get; set; } = "";
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public bool Revoked { get; set; }
    }
}
