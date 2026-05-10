using System;
using System.Threading.Tasks;
using MEPAuto.Contracts.Auth;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MEPAuto.Server.Api.Controllers
{
    [ApiController]
    [Route("api/v1/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IUserRepository _users;
        private readonly ILicenseService _licenses;
        private readonly IJwtTokenService _tokens;
        private readonly IAuditLogger _audit;

        public AuthController(IUserRepository users, ILicenseService licenses, IJwtTokenService tokens, IAuditLogger audit)
        {
            _users = users;
            _licenses = licenses;
            _tokens = tokens;
            _audit = audit;
        }

        [HttpPost("login"), AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            var user = await _users.GetByEmail(req.Email);
            if (user == null || user.Disabled || !BCryptPasswordHasher.Verify(req.Password, user.PasswordHash))
            {
                await _audit.Log(null, "auth.login", new { req.Email }, "fail");
                return Unauthorized(new { error = "invalid_credentials" });
            }

            var features = await _licenses.GetFeatures(user.Email);
            var accessToken = _tokens.CreateAccessToken(user, features, out var expiresAt);
            var refreshToken = await _tokens.CreateRefreshToken(user.UserId);

            user.LastLoginAt = DateTime.UtcNow;
            await _users.Save(user);
            await _audit.Log(null, "auth.login", new { user.UserId, user.Email }, "ok");

            return Ok(new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
            });
        }

        [HttpPost("refresh"), AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest req)
        {
            var userId = await _tokens.ValidateRefreshToken(req.RefreshToken);
            if (userId == null) return Unauthorized(new { error = "invalid_refresh_token" });

            var user = await _users.GetById(userId);
            if (user == null || user.Disabled) return Unauthorized(new { error = "user_disabled" });

            var features = await _licenses.GetFeatures(user.Email);
            var accessToken = _tokens.CreateAccessToken(user, features, out var expiresAt);
            return Ok(new LoginResponse
            {
                AccessToken = accessToken,
                RefreshToken = req.RefreshToken,
                ExpiresAt = expiresAt,
            });
        }

        [HttpPost("logout"), Authorize]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest req)
        {
            await _tokens.RevokeRefreshToken(req.RefreshToken);
            await _audit.Log(User, "auth.logout");
            return Ok();
        }

        [HttpGet("heartbeat"), Authorize]
        public IActionResult Heartbeat() => Ok(new HeartbeatResponse());
    }
}
