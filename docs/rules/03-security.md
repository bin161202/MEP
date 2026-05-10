# Rule 03 — Security: JWT + License + Audit + Online enforcement

> **TL;DR**: Mọi feature **bắt buộc qua VPS**. JWT bearer auth (TTL 30min), refresh token (8h, revoke-able), license check inline ở controller, audit log mọi request có auth. Client heartbeat 30s — fail 3 lần → grey-out ribbon. KHÔNG có offline cache.

## Authentication flow

```
Login (lúc Revit start hoặc cache hết hạn)
─────────────────────────────────────────
Client                                    Server
LoginDialog.OnLoginClick
  → POST /api/v1/auth/login {email,pwd} ─→ AuthController.Login
                                              BCrypt.Verify password
                                              CreateAccessToken (HS256, 30min, embed claims)
                                              CreateRefreshToken (32-byte random, 8h, store)
                                              AuditLog "auth.login" ok
                            ←──────── 200 {accessToken, refreshToken, expiresAt}
JwtCache.Save (DPAPI encrypt)
%LocalAppData%\MEPAuto\jwt.dat


Mỗi request feature
───────────────────
Client                                    Server
ServerProxy.Post (inject Authorization: Bearer <accessToken>)
  → POST /api/v1/feature/execute ────────→ JwtBearerAuth middleware validate
                                              → ClaimsPrincipal {sub, email, licenses}
                                            FeatureController:
                                              license check inline (CanUse)
                                              call Service → audit log → response
                            ←──────── 200 / 401 / 403


Refresh khi 401 (access token hết hạn, refresh chưa)
────────────────────────────────────────────────────
Client                                    Server
ServerProxy.TryRefresh
  → POST /api/v1/auth/refresh ───────────→ AuthController.Refresh
                                              ValidateRefreshToken
                                              CreateAccessToken mới
                            ←──────── 200 {accessToken mới, refreshToken giữ nguyên}
JwtCache.Save (update accessToken)
Retry POST feature/execute (1 lần)
```

## Quy tắc cứng

1. **Mọi endpoint** trừ `/health` + `/api/v1/auth/login` + `/api/v1/auth/refresh` có `[Authorize]`. Không có "feature endpoint không cần auth".

2. **License check** inline ở controller:
   ```csharp
   if (!await _license.CanUse(User, "{feature}.basic"))
       return StatusCode(403, new { error = "license_required", feature = "{feature}.basic" });
   ```

3. **Audit log** mọi request có auth qua `AuditMiddleware` (auto). Service-level audit cho action nghiệp vụ qua `IAuditLogger.Log(user, "feature.action", data)`. KHÔNG bao giờ log password/token raw.

4. **JWT signing key** đọc từ env var `JWT__SIGNING_KEY` (≥ 32 byte). Dùng `EnvOrConfig` helper thay vì `Configuration["Jwt:SigningKey"]` direct (Linux container quirk).

5. **Online enforcement strict**:
   - Heartbeat 30s gọi `GET /api/v1/auth/heartbeat`
   - Fail 3 lần liên tiếp (90s) → `IsOnline = false` → BaseFeatureCommand return Cancelled với "Mất kết nối server MEPAuto"
   - Mất mạng > 30 phút → access token hết hạn + refresh fail → force re-login
   - **KHÔNG có offline cache feature execution**

## Setup user + license (Phase 1)

```bash
# Sinh BCrypt hash trên VPS
ssh root@<vps-ip>
apt install -y python3-bcrypt
HASH=$(python3 -c "import bcrypt; print(bcrypt.hashpw(b'<password>', bcrypt.gensalt(11)).decode())")

# Edit /var/mepauto-data/users.json
cat > /var/mepauto-data/users.json <<EOF
[
  {
    "userId": "u-001",
    "email": "user@company.com",
    "passwordHash": "$HASH",
    "displayName": "User Name",
    "disabled": false,
    "createdAt": "2026-05-09T00:00:00Z",
    "lastLoginAt": null
  }
]
EOF

cat > /var/mepauto-data/licenses.json <<EOF
{
  "user@company.com": ["helloworld.basic", "ductrouting.basic"]
}
EOF

chown 1000:1000 /var/mepauto-data/*.json
chmod 600 /var/mepauto-data/*.json
docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml restart api
```

## Threat model

| Attack | Mitigation |
|---|---|
| Stolen JWT token | TTL 30min hạn chế blast radius. Refresh token revoke-able qua DataStorage. |
| Brute force login | Nginx rate limit `10 req/min` cho `/api/v1/auth/login`. |
| Replay attack | JWT có `jti` claim (unique per token). |
| Reverse engineer Client DLL | Phase 1 chấp nhận risk (Client thin, IP nặng ở server). M2: code obfuscation Client. |
| Server compromise | DPAPI encrypt JWT cache user-side. Phase 2 thêm token rotation. |
| MITM | Phase 1 HTTP-only test → migrate HTTPS với Let's Encrypt khi có domain. Production BẮT BUỘC TLS. |

## Reference

- `src/server/MEPAuto.Server.Api/Controllers/AuthController.cs` — login/refresh/heartbeat
- `src/server/MEPAuto.Server.Api/Auth/JwtTokenService.cs` — token gen + validate
- `src/server/MEPAuto.Server.Api/Middleware/AuditMiddleware.cs` — auto audit
- `src/client/MEPAuto.Client.Common/Auth/JwtCache.cs` — DPAPI encrypted local cache
- `src/client/MEPAuto.Client.Common/Auth/HeartbeatService.cs` — 30s timer + grey-out logic
- `src/client/MEPAuto.Client.Common/Commands/BaseFeatureCommand.cs` — check IsOnline + EnsureLoggedIn
