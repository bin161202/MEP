using System;
using System.Collections.Generic;

namespace MEPAuto.Server.Core.Models
{
    /// <summary>
    /// User account — Phase 1 lưu JSON file ở /var/mepauto-data/users.json.
    /// PasswordHash dùng BCrypt (KHÔNG bao giờ lưu plain text).
    /// </summary>
    public class User
    {
        public string UserId { get; set; } = "";
        public string Email { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Disabled { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
    }

    /// <summary>License entitlement — map từ user/email → list feature được phép dùng.</summary>
    public class LicenseEntitlement
    {
        public string Email { get; set; } = "";
        public List<string> Features { get; set; } = new List<string>();
    }

    /// <summary>1 audit entry — append vào /var/mepauto-data/audit.log dạng JSON-line.</summary>
    public class AuditEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string Action { get; set; } = "";
        public string? Status { get; set; }
        public int DurationMs { get; set; }
        public Dictionary<string, object?> Data { get; set; } = new Dictionary<string, object?>();
    }
}
