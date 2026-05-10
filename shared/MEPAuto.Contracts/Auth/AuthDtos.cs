using System;

namespace MEPAuto.Contracts.Auth
{
    /// <summary>Wire format login: email + password.</summary>
    public class LoginRequest
    {
        public string Email { get; set; } = "";
        public string Password { get; set; } = "";
    }

    /// <summary>Wire format response cho login + refresh.</summary>
    public class LoginResponse
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>Wire format refresh + logout.</summary>
    public class RefreshRequest
    {
        public string RefreshToken { get; set; } = "";
    }

    /// <summary>Wire format heartbeat response.</summary>
    public class HeartbeatResponse
    {
        public DateTime ServerTime { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "ok";
    }
}
