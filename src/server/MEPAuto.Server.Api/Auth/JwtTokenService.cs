using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.Core.Auth;
using MEPAuto.Server.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MEPAuto.Server.Api.Auth
{
    /// <summary>
    /// HS256 JWT issuer + opaque refresh token.
    /// Refresh token là 32-byte random (base64url) lưu trong IDataStorageService → revoke được.
    /// </summary>
    public class JwtTokenService : IJwtTokenService
    {
        private const string RefreshKeyPrefix = "refresh/";
        private readonly JwtOptions _opts;
        private readonly IDataStorageService _storage;

        public JwtTokenService(IOptions<JwtOptions> opts, IDataStorageService storage)
        {
            _opts = opts.Value;
            _storage = storage;
            if (string.IsNullOrWhiteSpace(_opts.SigningKey) || _opts.SigningKey.Length < 32)
                throw new InvalidOperationException("Jwt:SigningKey phải đặt và >= 32 byte. Set qua env JWT__SIGNING_KEY.");
        }

        public string CreateAccessToken(User user, string[] features, out DateTime expiresAt)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            expiresAt = DateTime.UtcNow.AddMinutes(_opts.AccessTokenMinutes);
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("licenses", string.Join(",", features)),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            };
            var token = new JwtSecurityToken(
                issuer: _opts.Issuer,
                audience: _opts.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAt,
                signingCredentials: creds);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<string> CreateRefreshToken(string userId)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            var token = Base64UrlEncoder.Encode(bytes);
            var state = new RefreshTokenState
            {
                Token = token,
                UserId = userId,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(_opts.RefreshTokenHours),
                Revoked = false,
            };
            await _storage.Set(RefreshKeyPrefix + token, state);
            return token;
        }

        public async Task<string?> ValidateRefreshToken(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return null;
            var state = await _storage.Get<RefreshTokenState>(RefreshKeyPrefix + refreshToken);
            if (state == null || state.Revoked || DateTime.UtcNow >= state.ExpiresAt) return null;
            return state.UserId;
        }

        public async Task RevokeRefreshToken(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return;
            var state = await _storage.Get<RefreshTokenState>(RefreshKeyPrefix + refreshToken);
            if (state == null) return;
            state.Revoked = true;
            await _storage.Set(RefreshKeyPrefix + refreshToken, state);
        }
    }
}
