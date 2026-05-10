# Deploy walkthrough — MEPAuto VPS (shared VPS với EPAuto, port 8081)

MEPAuto và EPAuto dùng chung 1 VPS Ubuntu 24.04. EPAuto đã chiếm port 8080 + nginx container trên port 80.
MEPAuto API container bind `127.0.0.1:8081:8080` — nginx system proxy từ domain MEPAuto tới 8081.
Default variant: `system` (KHÔNG phải `container`).

## Tóm tắt đường đi

```
Máy LEAD (Win)              Droplet (Ubuntu 24.04) — đã có EPAuto
─────────────────           ──────────────────────
1. Build code               (EPAuto đang chạy — KHÔNG đụng)
3. Rsync lên /opt/mepauto   4. Tạo .env + run deploy.sh system
                            5. Seed user qua seed-user.ps1
6. Test login từ curl (port 8081)
7. Cài MSI lên máy có Revit → test ribbon end-to-end
```

---

## Step 1 — LEAD: kiểm tra EPAuto không bị ảnh hưởng

```bash
# SSH VPS kiểm tra EPAuto vẫn chạy
ssh root@<DROPLET_IP> 'curl -s http://127.0.0.1/health | head -c 200'
# Expect: {"status":"ok","service":"epauto-api",...}

ssh root@<DROPLET_IP> 'ss -tlnp | grep -E "8080|8081"'
# Expect: epauto-api đang nghe 127.0.0.1:8080, 8081 CÒN TRỐNG
```

---

## Step 2 — VPS: đảm bảo Docker + system nginx đã cài (EPAuto đã cài sẵn)

```bash
ssh root@<DROPLET_IP>
docker --version          # Expect: Docker version 27.x
docker compose version    # Expect: Docker Compose version v2.x
nginx -v                  # Expect: nginx/1.x.x (đã cài cho EPAuto)
```

Nếu VPS mới chưa có Docker (trường hợp EPAuto chưa deploy):

```bash
apt update
apt install -y ca-certificates curl gnupg
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu noble stable" \
    | tee /etc/apt/sources.list.d/docker.list > /dev/null
apt update
apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin nginx
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
```

---

## Step 3 — LEAD: rsync code lên VPS

Từ máy LEAD (Git Bash hoặc WSL):

```bash
cd "d:/MEP Add-in/MEP"
rsync -avz --progress \
    --exclude='bin/' --exclude='obj/' --exclude='.git/' --exclude='*.user' \
    --exclude='tools/deploy/.env' --exclude='tools/deploy/VPS-INVENTORY.md' \
    MEPAuto/ root@<DROPLET_IP>:/opt/mepauto/
```

Verify trên VPS:

```bash
ssh root@<DROPLET_IP> 'ls -la /opt/mepauto/ && head -3 /opt/mepauto/MEPAuto.sln'
```

---

## Step 4 — VPS: tạo .env + chạy deploy.sh system

```bash
ssh root@<DROPLET_IP>
cd /opt/mepauto/tools/deploy

# Sinh JWT signing key riêng cho MEPAuto (KHÔNG dùng key của EPAuto):
JWT_KEY=$(openssl rand -base64 48)
echo "Generated MEPAuto key (lưu offline): $JWT_KEY"

# Tạo .env
cat > .env <<EOF
JWT_SIGNING_KEY=$JWT_KEY
JWT_ISSUER=https://api.mepauto.local
JWT_AUDIENCE=mepauto-client
DOMAIN=api.mepauto.local
NGINX_CONF=nginx-http-only.conf
EOF
chmod 600 .env

# Tạo /var/mepauto-data trống
mkdir -p /var/mepauto-data
echo '[]' > /var/mepauto-data/users.json
echo '{}' > /var/mepauto-data/licenses.json
chmod 600 /var/mepauto-data/*.json
chown -R 1000:1000 /var/mepauto-data
chmod 700 /var/mepauto-data

# Build + up (system variant — KHÔNG đụng nginx container EPAuto)
chmod +x deploy.sh
./deploy.sh system
```

Smoke test tại VPS (port 8081 — không qua nginx):

```bash
curl http://127.0.0.1:8081/health
# Expect: {"status":"ok","service":"mepauto-api",...}

curl http://127.0.0.1:8081/api/v1/helloworld/execute
# Expect: 401 (Unauthorized — chưa có token)
```

Test từ máy LEAD (qua nginx system nếu đã config site):

```bash
curl http://<DROPLET_IP>:<nginx-mepauto-port>/health
```

Nếu fail → check log:

```bash
docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml logs api | tail -50
```

---

## Step 5 — VPS hoặc LEAD: seed user đầu tiên

**Cách A — chạy seed-user.ps1 trên máy LEAD** (yêu cầu PowerShell + đã build server):

```powershell
cd "d:\MEP Add-in\MEP\MEPAuto"
dotnet build src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj -c Release
.\tools\deploy\seed-user.ps1 -Email "lead@mepauto.test" -DisplayName "LEAD" -Features helloworld.basic
# Script in JSON snippet → copy vào /var/mepauto-data/users.json + licenses.json trên VPS.
```

**Cách B — sinh hash trực tiếp trên VPS**:

```bash
ssh root@<DROPLET_IP>
apt install -y python3-bcrypt python3-pip
python3 -c "import bcrypt; print(bcrypt.hashpw(b'<your-password>', bcrypt.gensalt(11)).decode())"
```

Edit `/var/mepauto-data/users.json`:

```json
[
  {
    "userId": "u-lead001",
    "email": "lead@mepauto.test",
    "passwordHash": "<bcrypt-hash>",
    "displayName": "LEAD",
    "disabled": false,
    "createdAt": "2026-05-09T00:00:00Z",
    "lastLoginAt": null
  }
]
```

Edit `/var/mepauto-data/licenses.json`:

```json
{
  "lead@mepauto.test": ["helloworld.basic"]
}
```

```bash
chown -R 1000:1000 /var/mepauto-data
chmod 600 /var/mepauto-data/*.json
docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml restart api
```

---

## Step 6 — verify auth flow từ curl

```bash
# Login (port 8081 trực tiếp)
curl -X POST http://<DROPLET_IP>:8081/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"lead@mepauto.test","password":"<your-password>"}' | jq

# Lưu token
TOKEN=$(curl -s -X POST http://127.0.0.1:8081/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"lead@mepauto.test","password":"<your-password>"}' | jq -r .accessToken)

# Heartbeat
curl http://127.0.0.1:8081/api/v1/auth/heartbeat -H "Authorization: Bearer $TOKEN"
# Expect: 200 {"serverTime":"...","status":"ok"}

# Hello-World execute
curl -X POST http://127.0.0.1:8081/api/v1/helloworld/execute \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"snapshot":{"userName":"LEAD"}}' | jq
# Expect: {"message":"Xin chào LEAD, server time: ...","jobId":"..."}

# Verify audit log
ssh root@<DROPLET_IP> 'tail /var/mepauto-data/audit.log'
```

✅ Nếu cả 4 curl pass → API deploy DONE.

---

## Step 7 — verify pilot end-to-end (cần máy có Revit)

### Cách A — Manual dev install (FASTEST, không cần WiX)

```powershell
cd "d:\MEP Add-in\MEP\MEPAuto"
dotnet build MEPAuto.sln -c Release-2024

# Copy DLL → %LocalAppData%\MEPAuto\2024
$src = "src\client\MEPAuto.Client.Shell\bin\Release-2024\"
$dst = "$env:LocalAppData\MEPAuto\2024\"
New-Item -ItemType Directory -Force -Path $dst
Copy-Item "$src*.dll" -Destination $dst

# Cài .addin manifest → %AppData%\Autodesk\Revit\Addins\2024
$addinSrc = "installer\addin-manifests\MEPAuto-2024.addin"
$addinDst = "$env:AppData\Autodesk\Revit\Addins\2024\MEPAuto-2024.addin"
New-Item -ItemType Directory -Force -Path (Split-Path $addinDst)
Copy-Item $addinSrc $addinDst

# Patch <Assembly> placeholder
$addinXml = Get-Content $addinDst -Raw
$addinXml = $addinXml -replace '__MEPAUTO_INSTALL_PATH__', "$dst`MEPAuto.Client.Shell.dll"
$addinXml | Set-Content $addinDst -Encoding utf8

# Trỏ Client tới VPS (port 8081)
$config = "$env:LocalAppData\MEPAuto\config.json"
New-Item -ItemType Directory -Force -Path (Split-Path $config)
'{ "ServerBaseUrl": "http://<DROPLET_IP>:8081" }' | Set-Content $config
```

Mở Revit 2024 → tab "MEPAuto" → panel "MEPAuto - Demo" → click "Hello World":
1. LoginDialog hiện → nhập credentials LEAD → JWT cache vào DPAPI.
2. Click button → TaskDialog **"Xin chào lead@mepauto.test, server time: ..."** ✅
3. Verify audit log VPS: `tail /var/mepauto-data/audit.log` → thấy `helloworld.execute`.

Test offline:
1. Tắt internet → đợi 90s → click button → TaskDialog "Mất kết nối server MEPAuto..."
2. Bật lại internet → đợi 30s → click → work.

### Cách B — MSI installer

```powershell
cd "d:\MEP Add-in\MEP\MEPAuto\installer"
./Build-MSI.ps1
```

→ Output `MEPAuto-Setup.msi`. Lúc cài user chọn:
- **Just me**: DLL → `%LocalAppData%\MEPAuto\{ver}\`, `.addin` → `%AppData%\Autodesk\Revit\Addins\{ver}\`.
- **All users** (UAC): DLL → `C:\Program Files\MEPAuto\{ver}\`, `.addin` → `%ProgramData%\Autodesk\Revit\Addins\{ver}\`.

---

## Nginx system site config (khi có domain MEPAuto)

Khi có domain riêng cho MEPAuto (vd `api.mepauto.io`):

```bash
ssh root@<DROPLET_IP>
# Dùng template sinh sẵn
DOMAIN=api.mepauto.io envsubst < /opt/mepauto/tools/deploy/nginx-system-site.conf.template \
    > /etc/nginx/sites-available/mepauto-api
ln -s /etc/nginx/sites-available/mepauto-api /etc/nginx/sites-enabled/
nginx -t && nginx -s reload
```

Template `nginx-system-site.conf.template` đã có sẵn trong repo — proxy tới `127.0.0.1:8081`.

---

## Troubleshooting

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `docker compose up` báo `port 8081 already in use` | Process khác chiếm 8081 | `lsof -i :8081` → dừng process đó |
| API container crash loop | `JWT__SIGNING_KEY < 32 byte` | Sinh key mới: `openssl rand -base64 48` |
| Login trả 401 luôn | passwordHash không khớp | Re-hash (BCrypt work factor 11) + paste lại JSON |
| Heartbeat 200 nhưng feature 403 | License chưa cấp | Edit `/var/mepauto-data/licenses.json` thêm feature + restart api |
| Client báo "Mất kết nối server MEPAuto" sau 90s | Heartbeat fail 3x | Check config.json trỏ đúng URL:8081, ufw mở port, `docker logs mepauto-api` |
| EPAuto (port 8080) bị ảnh hưởng | Sai container hoặc docker-compose file | Đảm bảo dùng `docker-compose.system-nginx.yml` KHÔNG phải `.yml` EPAuto |
