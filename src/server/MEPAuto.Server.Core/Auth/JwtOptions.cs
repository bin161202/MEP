namespace MEPAuto.Server.Core.Auth
{
    /// <summary>Bind từ section "Jwt" trong appsettings + override env var JWT__SIGNING_KEY.</summary>
    public class JwtOptions
    {
        public string SigningKey { get; set; } = "";
        public string Issuer { get; set; } = "https://api.mepauto.local";
        public string Audience { get; set; } = "mepauto-client";
        public int AccessTokenMinutes { get; set; } = 30;
        public int RefreshTokenHours { get; set; } = 8;
    }
}
