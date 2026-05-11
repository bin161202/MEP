# Dev Local Setup — Test feature mới KHÔNG cần đụng VPS

Cẩm nang setup môi trường dev local trên máy member để test feature mới **trước khi push code**.
Hoàn toàn không cần SSH vào VPS production.

## Vì sao cần

Khi member tạo feature mới (vd `ToiletStackConnect`):
- Build client → ribbon hiện button ngay (RAM reload + restart Revit)
- Nhưng server VPS **chưa biết endpoint mới** → click button bị 404
- Push code lên VPS để test = chậm (cần LEAD review + deploy)

→ Chạy server **trên máy mình** với code feature mới + trỏ client về local → test ngay 15-30s/iteration.

## Setup 1 lần (~2 phút)

### Bước 1 — Cấu hình client trỏ về local

Đóng Revit. Mở `%LocalAppData%\MEPAuto\config.json`, sửa:

```json
{ "ServerBaseUrl": "http://localhost:5050" }
```

(Dùng port 5050 thay vì 5000 để tránh xung đột với dotnet dev default 5000/5001.)

## Daily loop khi dev feature mới

### Bước 1 — Build + run server local

Terminal 1 (để chạy nguyên):
```powershell
cd "d:\MEP Add-in\Plumbing\MEPAuto\src\server\MEPAuto.Server.Api"
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run -c Debug-2024 --urls "http://localhost:5050"
```

Mong đợi console:
```
Now listening on: http://localhost:5050
Application started.
```

**Lần đầu chạy** server sẽ tự copy users + licenses từ `tools/dev-seed/` xuống `data-dev/` —
login bằng **cùng credentials VPS production** (không cần tạo user dev riêng):

```
AutoSeedDevUser: đã copy seed từ ...\tools\dev-seed → ...\data-dev.
Login với credentials VPS production.
```

### Bước 2 — Khi thêm feature mới: cấp license cho user

Trong terminal khác (server vẫn đang chạy ở terminal 1):

```powershell
cd "d:\MEP Add-in\Plumbing\MEPAuto\src\server\MEPAuto.Server.Api"
$env:ASPNETCORE_ENVIRONMENT="Development"
dotnet run -c Debug-2024 -- seed-user `
    --email "minhduyforbusiness@gmail.com" `
    --password "DnBIMcore21@CLC" `
    --features "helloworld.basic,drainurinal.basic,toiletstackconnect.basic"
```

Chạy lại CLI cho **CÙNG email** = UPDATE (giữ UserId, password + features mới). Không tạo trùng.

Restart server (Ctrl+C terminal 1 → chạy lại) để load license mới.

### Bước 3 — Test feature trong Revit

Mở Revit → click button feature → dialog login → footer ghi `Server: http://localhost:5050`
(để bạn biết đang trỏ local, không phải VPS). Đăng nhập bằng credentials VPS production.

Click button → server local đáp ứng (có code feature mới) → test OK.

### Bước 4 — Sau khi feature OK, switch về VPS

```json
// %LocalAppData%\MEPAuto\config.json
{ "ServerBaseUrl": "http://129.212.230.159:8081" }
```

→ Commit + push code → LEAD review + deploy server VPS + cấp license production.

## CLI seed-user reference

```
dotnet run -- seed-user --email <X> --password <Y> [options]
```

| Flag | Bắt buộc | Mô tả |
|---|---|---|
| `--email <email>` | ✓ | Email user (vd `minhduy@local.dev`) |
| `--password <plaintext>` | ✓ | Password — auto hash BCrypt cost=11 |
| `--features <csv>` | | License keys cách bởi dấu phẩy, vd `helloworld.basic,toiletstackconnect.basic` |
| `--display <name>` | | DisplayName trong `users.json` |
| `--data-dir <path>` | | Override DataDir (default theo `appsettings.{Env}.json`) |

Exit code: `0` = OK, `2` = thiếu/sai argument.

## Troubleshoot

| Triệu chứng | Nguyên nhân | Fix |
|---|---|---|
| `dotnet run` báo `Address already in use` | Port 5050 bị process khác chiếm | `Get-NetTCPConnection -LocalPort 5050` tìm PID → `Stop-Process` |
| Server start nhưng `/health` lỗi `Jwt:SigningKey không có` | `ASPNETCORE_ENVIRONMENT` chưa set Development | `$env:ASPNETCORE_ENVIRONMENT="Development"` trước `dotnet run` |
| Login 401 `Invalid email or password` | Password VPS đổi sau khi seed local → hash cũ | Xóa `data-dev/` → restart server (auto-seed lại) HOẶC chạy `seed-user` với password mới |
| Server log "không tìm thấy tools/dev-seed/" | Repo thiếu folder seed | `git pull` lấy commit mới; hoặc chạy `seed-user` thủ công |
| Click feature 404 | Endpoint Controller server-side viết sai route | Check `[Route("api/v1/{featurename}")]` trong `{Feature}Controller.cs` |
| Click feature 403 | License key trong `licenses.json` không khớp key check trong Controller | Đọc Controller xem key gì, seed lại với đúng key |
| Switch VPS lại bị `localhost:5050` | Quên sửa `%LocalAppData%\MEPAuto\config.json` | Edit lại file |

## Bảo mật

- `data-dev/users.json` + `licenses.json` ở **máy local**, đã `.gitignore` → không commit lên repo.
- `tools/dev-seed/users.json` + `licenses.json` **CÓ commit** (để member git pull về là có user sẵn).
  Chứa **hash BCrypt cost=11** — không plaintext. Brute-force unfeasible với password strong.
- Khi LEAD đổi password VPS hoặc thêm user → pull lại `tools/dev-seed/` từ VPS + commit để team sync.
- JWT signing key dev = `DEV_KEY_NOT_FOR_PROD_...` trong `appsettings.Development.json` — **KHÁC** key production VPS.
- Token sinh ra ở local KHÔNG valid trên VPS (issuer/audience khác) → isolation an toàn.
