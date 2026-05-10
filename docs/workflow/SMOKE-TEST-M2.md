# Smoke test M2 end-to-end — checklist cho LEAD

Sau khi merge tất cả PR M2 vào `main`, chạy theo checklist này để verify từng phase. Tổng thời gian: ~60 phút.

## Tiền điều kiện

- [ ] Local build sạch: `dotnet build MEPAuto.sln -c Release-2024` 0 warning
- [ ] CI `main` xanh (cả 3 job: build-server, build-client matrix, lint)
- [ ] Server tests pass trong CI
- [ ] SSH key tới VPS work (`ssh root@129.212.230.159 echo ok` → `ok`)
- [ ] Máy có Revit 2024 đã cài
- [ ] WiX v4 CLI cài local (`dotnet tool install --global wix`)

---

## Bước 1 — Deploy server changes lên VPS

```powershell
powershell -ExecutionPolicy Bypass -File tools/deploy/sync-m2-to-vps.ps1 -DryRun  # xem trước
powershell -ExecutionPolicy Bypass -File tools/deploy/sync-m2-to-vps.ps1          # chạy thật
```

**Verify**:
- [ ] Output cuối: `DONE — M2 server changes synced.`
- [ ] `/health` (port 8081) trả JSON với fields `status`, `version`, `uptime`
- [ ] `/api/v1/version/check?current=0.0.0` trả JSON
- [ ] EPAuto (port 8080) vẫn hoạt động bình thường: `curl http://127.0.0.1/health` (qua nginx EPAuto)

**Nếu fail**:
```bash
ssh root@129.212.230.159 "docker logs --tail 50 mepauto-api"
# Kiểm tra EPAuto không bị ảnh hưởng:
ssh root@129.212.230.159 "docker logs --tail 20 epauto-api"
```

---

## Bước 2 — Test endpoints (cần JWT)

```bash
# Login lấy JWT (port 8081)
TOKEN=$(curl -s -X POST http://129.212.230.159:8081/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@example.com","password":"<password>"}' | jq -r .accessToken)

# Heartbeat
curl -H "Authorization: Bearer $TOKEN" http://127.0.0.1:8081/api/v1/auth/heartbeat | jq
# Expect: {"serverTime":"...","status":"ok"}
```

**Verify**:
- [ ] Login trả accessToken valid
- [ ] Heartbeat trả 200
- [ ] Anonymous request trả 401

---

## Bước 3 — Test push tag thử end-to-end (CI/CD + Installer)

```bash
git tag v0.1.0-rc1
git push origin v0.1.0-rc1
```

Đợi GitHub Actions Release workflow ~10 phút.

**Verify ở CI log**:
- [ ] Step "Build MSI" thành công → file `MEPAuto-Setup-v0.1.0-rc1.msi`
- [ ] Step "Compute SHA256" tạo file `.sha256`
- [ ] Step "Create GitHub Release" thành công, badge **Pre-release**
- [ ] Release page có 2 file: `MEPAuto-Setup-v0.1.0-rc1.msi` + `.sha256`

---

## Bước 4 — Cài MSI lên Revit 2024

```powershell
$msi = "$env:TEMP\MEPAuto-Setup-v0.1.0-rc1.msi"
# Download từ GitHub Release page
msiexec /i $msi /qb MSIINSTALLPERUSER=1
```

**Verify**:
- [ ] `%LocalAppData%\MEPAuto\2024\MEPAuto.Client.Shell.dll` tồn tại
- [ ] `%AppData%\Autodesk\Revit\Addins\2024\MEPAuto-2024.addin` tồn tại, `<Assembly>` trỏ đúng

**Mở Revit 2024**:
- [ ] Tab "MEPAuto" xuất hiện trên ribbon
- [ ] Click HelloWorld button → thành công, gọi VPS port 8081, hiện response

---

## Bước 5 — Verify auto-update flow

### 5.1 Bump version.json trên VPS

```bash
ssh root@129.212.230.159 "nano /var/mepauto-data/version.json"
```

Set `latest: "0.1.0"` (cần có release v0.1.0 thực).

### 5.2 Verify client check

- [ ] Mở Revit → đợi 5-10s → dialog "Có bản cập nhật mới MEPAuto" xuất hiện
- [ ] 3 button: "Cập nhật ngay", "Để sau", "Bỏ qua X.X.X"
- [ ] Click "Cập nhật ngay" → download MSI → SHA256 verify → prompt đóng Revit

### 5.3 Rollback sau test

```bash
ssh root@129.212.230.159 "cat /var/mepauto-data/version.json"
# Rollback latest về thực tế
```

---

## Bước 6 — Cleanup

```bash
git tag -d v0.1.0-rc1
git push origin --delete v0.1.0-rc1
```

---

## Pass criteria

- ✅ Tất cả endpoints hoạt động (health, auth, version/check)
- ✅ MSI cài lên Revit 2024, ribbon load, HelloWorld work
- ✅ EPAuto không bị ảnh hưởng bởi MEPAuto deploy
- ✅ Auto-update flow hoàn chỉnh: bump VPS → dialog → download → install → verify version mới
