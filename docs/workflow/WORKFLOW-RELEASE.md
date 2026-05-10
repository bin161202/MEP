# Workflow release MEPAuto

Quy trình LEAD chạy mỗi lần ship phiên bản mới.

## 1. Chuẩn bị

- [ ] Tất cả PR cần thiết đã merge vào `main`.
- [ ] Section `## [Unreleased]` trong [CHANGELOG.md](../../CHANGELOG.md) có nội dung.
- [ ] CI `main` xanh.
- [ ] Local build sạch: `dotnet build MEPAuto.sln -c Release-2024` không warning + test pass.

## 2. Tag + push

Quyết định bump version theo SemVer:
- **MAJOR** (1.0.0 → 2.0.0): breaking change
- **MINOR** (0.1.0 → 0.2.0): feature mới
- **PATCH** (0.1.0 → 0.1.1): bug fix

```bash
cd "d:/MEP Add-in/MEP/MEPAuto"

# Cập nhật CHANGELOG: di chuyển [Unreleased] → "## [X.Y.Z] - YYYY-MM-DD"
git add CHANGELOG.md
git commit -m "chore(release): v0.1.5 — release notes"
git push

# Tag + push
git tag v0.1.5
git push origin v0.1.5
```

Workflow `Release` (`.github/workflows/release.yml`) tự động:
1. Build 6 config Release-2022..2027
2. WiX harvest + build MSI `MEPAuto-Setup-v0.1.5.msi`
3. msiexec validate
4. Compute SHA256 → file .sha256
5. Tạo GitHub Release với MSI + .sha256 + release notes từ CHANGELOG

Đợi ~10 phút.

## 3. Update VPS version.json

Đây là step **kích hoạt auto-update notification** cho user.

```bash
# Lấy SHA256 từ GitHub Release
SHA256=$(curl -sL https://github.com/MEP-Automation/MEPAuto/releases/download/v0.1.5/MEPAuto-Setup-v0.1.5.msi.sha256 | awk '{print $1}')

# SSH VPS update
ssh root@129.212.230.159 "nano /var/mepauto-data/version.json"
```

Nội dung mẫu (xem [tools/deploy/version.json.example](../../tools/deploy/version.json.example)):

```json
{
  "latest": "0.1.5",
  "minSupported": "0.1.0",
  "downloadUrlPattern": "https://github.com/MEP-Automation/MEPAuto/releases/download/v{version}/MEPAuto-Setup-v{version}.msi",
  "sha256ByVersion": {
    "0.1.4": "abc...",
    "0.1.5": "def..."
  },
  "releaseNotes": "## 0.1.5\n\n- Tính năng X\n- Fix bug Y",
  "revitVersions": ["2022", "2023", "2024", "2025", "2026", "2027"]
}
```

VersionService cache 60s → user thấy update notice trong 90s.

Hoặc dùng script tự động:
```powershell
powershell -ExecutionPolicy Bypass -File tools/deploy/update-version-json.ps1 -Version 0.1.5
```

## 4. Verify trên máy thật

- [ ] Mở Revit 2024 trên máy có version cũ → notification "Có bản mới 0.1.5" xuất hiện.
- [ ] Click "Cập nhật ngay" → MSI download → SHA256 verify pass → prompt đóng Revit.
- [ ] Đóng Revit → MSI tự chạy → mở lại Revit → ribbon MEPAuto load với version mới.

## 5. Rollback emergency

```bash
# Quick rollback
ssh root@129.212.230.159 "sed -i 's/\"latest\": \"0.1.5\"/\"latest\": \"0.1.4\"/' /var/mepauto-data/version.json"
```

## 6. Mandatory upgrade

```json
{
  "latest": "0.1.5",
  "minSupported": "0.1.5"  // ← user dưới 0.1.5 bị block
}
```

---

## Checklist nhanh

- [ ] CHANGELOG di chuyển Unreleased → version mới
- [ ] git tag vX.Y.Z + push
- [ ] Đợi GitHub Actions Release workflow xanh
- [ ] Copy SHA256 từ release page
- [ ] SSH VPS update `/var/mepauto-data/version.json`
- [ ] Test thực tế trên máy có Revit cài
