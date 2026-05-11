# MEMBER Dev Workflow — 7 bước daily

Cẩm nang vòng lặp dev nhanh **15-30s/iteration** (so với 1-2 phút restart Revit truyền thống).

## Tiền điều kiện (LEAD setup 1 lần)

- ✅ Repo MEPAuto monorepo build pass `dotnet build MEPAuto.sln -c Release-2024`
- ✅ VPS Phase 1 chạy tại `http://<vps-ip>:8081` — `/health` 200
- ✅ User test có account + license cho feature đang làm (LEAD seed)
- ✅ MSI cài hoặc DLL copy thủ công (xem `tools/deploy/DEPLOY-WALKTHROUGH.md` Step 7)
- ✅ RevitAddinManager (RAM) cài cho mỗi version Revit dùng


## Khi dev feature mới chưa push lên VPS

Server VPS chưa có endpoint feature mới → click button bị 404. Đừng push code chỉ để test —
chạy server **trên máy mình** thay VPS. Setup 5 phút, không cần SSH VPS:

👉 Xem **[DEV-LOCAL-SETUP.md](DEV-LOCAL-SETUP.md)** — `dotnet run -- seed-user` + cấu hình client trỏ local.
## 7 bước daily loop

```
                    ┌──────────────────────────────┐
                    │ 1. Sửa code feature trong VS │
                    └─────────────┬────────────────┘
                                  │
                                  v
                    ┌──────────────────────────────┐
                    │ 2. Ctrl+Shift+B build sln    │
                    │    (KHÔNG F5 — không cần     │
                    │    debug; nhanh hơn)         │
                    └─────────────┬────────────────┘
                                  │
                                  v
                    ┌──────────────────────────────┐
                    │ 3. Trong Revit (đang chạy):  │
                    │    Add-Ins tab → Add-In Mgr  │
                    │    → Reload                  │
                    └─────────────┬────────────────┘
                                  │
                                  v
                    ┌──────────────────────────────┐
                    │ 4. Click ribbon button        │
                    │    feature đang làm → test    │
                    └─────────────┬────────────────┘
                                  │
                          Pass? Done.
                          Need verify nhiều case?
                                  │ yes
                                  v
                    ┌──────────────────────────────┐
                    │ 5. Auto-test nhiều scenario  │
                    │    qua MCP (optional)        │
                    └─────────────────────────────┘
```

## Chi tiết từng bước

### 1. Sửa code

Edit trong:
- `src/client/features/MEPAuto.{Feature}/Commands/{Feature}Command.cs`
- `src/client/features/MEPAuto.{Feature}/Contracts/{Feature}Contract.cs`
- `src/server/features/MEPAuto.Server.{Feature}/Application/{Feature}Service.cs`
- `src/server/features/MEPAuto.Server.{Feature}/Domain/{Feature}Logic.cs`

### 2. Build

`Ctrl+Shift+B` trong VS → build full solution → đảm bảo Client.Shell pickup transitive copy DLL.

Nếu chỉ sửa server-side:
```bash
dotnet build src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj -c Release
# Rebuild + restart container trên VPS:
ssh root@<vps-ip> 'cd /opt/mepauto/tools/deploy && docker compose -f docker-compose.system-nginx.yml build api && docker compose -f docker-compose.system-nginx.yml up -d'
```

### 3. Reload qua RevitAddinManager

Trong Revit:
1. Tab **Add-Ins** → "External Tools" → click **Add-In Manager (Manual Mode)**
2. Tab "Loaded Addins": tìm `MEPAuto.{Feature}.dll`
3. Click **Reload** → DLL mới load đè cũ
4. Đóng dialog

→ **5-10s**, không restart Revit.

### 4. Test thủ công

Click button feature trên ribbon → verify TaskDialog kết quả. Verify kết quả Revit document khớp expected.

### 5. Fix loop

Fail → fix → quay lại bước 1. Pass → commit + push PR. LEAD review check Manifest đủ + Domain ở server + audit log có entry.

## Khi nào VẪN PHẢI restart Revit (~5-10% case)

- Sửa Manifest (Name, ServerEndpoint, Order, ...) — RAM reload không update ribbon
- Sửa `MEPAuto.Client.Shell` (RibbonBuilder, BaseFeatureCommand, AuthBootstrap)
- Sửa XAML embedded resource
- Đổi version Revit (2024 → 2025)

## Troubleshoot

| Symptom | Nguyên nhân | Fix |
|---|---|---|
| Click "Reload" trong RAM nhưng behavior cũ | DLL bị VS lock | Đóng VS hoặc stop process dùng DLL |
| RAM dialog không hiện feature DLL | DLL chưa copy vào install folder | Build sln + script copy DLL |
| TaskDialog "Mất kết nối server MEPAuto" | Heartbeat fail 3x | `curl http://<vps-ip>:8081/health` từ máy dev |
| TaskDialog "Không có quyền" (403) | License chưa cấp | Edit `/var/mepauto-data/licenses.json` thêm `{feature}.basic` → restart api |
| Revit treo sau click feature | Sync-over-async deadlock | Check `ConfigureAwait(false)` ở MỌI internal await Client |
| "No Transaction Attribute" dialog | Quên `[Transaction]` trên class concrete | Thêm `[Transaction(TransactionMode.Manual)]` direct trên `{Feature}Command` |

## Reference

- `docs/workflow/WORKFLOW-NEW-FEATURE.md` — checklist tạo feature mới từ A-Z
- `docs/rules/05-anti-patterns.md` — patterns tránh
- `tools/deploy/DEPLOY-WALKTHROUGH.md` — setup VPS
