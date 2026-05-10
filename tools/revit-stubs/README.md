# Revit stubs cho CI build + multi-version compile

Folder này chứa Revit reference DLL copy từ máy LEAD — cho CI build pass mà KHÔNG cần cài Revit
(GitHub Actions Windows runner không thể cài Revit + license phức tạp).

## Trạng thái stub hiện tại

| Version | Nguồn | Ghi chú |
|---|---|---|
| 2022 | Revit 2022 cài thật | DLL chính chủ |
| 2023 | Revit 2023 cài thật | DLL chính chủ |
| 2024 | Revit 2024 cài thật | DLL chính chủ |
| 2025 | Revit 2025 cài thật | DLL chính chủ |
| **2026** | **Copy từ 2025 (interim)** | ⚠️ Tạm dùng API 2025 — refresh khi có Revit 2026 thật |
| **2027** | **Copy từ 2025 (interim)** | ⚠️ Tạm dùng API 2025 — refresh khi có Revit 2026/2027 thật |

## Trade-off của interim stub 2026/2027

- ✅ **Build pass**: Client code compile được cho cả 6 version 2022-2027 → CI không gãy + MSI ship được 6 nhánh.
- ⚠️ **Runtime risk trên Revit 2026/2027 thật**: nếu Autodesk có breaking change API ở 2026/2027 (rename method, đổi signature, deprecate type), DLL ta build với stub 2025 sẽ chạy fail trên Revit 2026/2027 thật với `MissingMethodException` / `TypeLoadException`. Hiện code MEPAuto chỉ dùng API có từ 2025 → trade-off chấp nhận được.
- ⚠️ **`ElementId.Value` (long)** đã có từ 2025 → 2026/2027 OK. Đó là breaking change lớn nhất gần đây.

## Khi nào refresh stub

| Tình huống | Hành động |
|---|---|
| Cài thêm Revit 2026 trên máy LEAD | Chạy lại script + commit |
| Cài thêm Revit 2027 trên máy LEAD | Chạy lại script + commit |
| Bắt đầu ship MSI cho user dùng Revit 2026/2027 thật | **BẮT BUỘC** refresh stub thật + smoke test runtime trên Revit 2026/2027 |
| Phát hiện bug runtime trên Revit 2026 (test thật) | Refresh stub 2026, rebuild, test lại |

## Cách refresh

```powershell
# Trên máy LEAD có Revit cài thật
# Copy DLL từ Program Files Revit tương ứng
$versions = @("2022", "2023", "2024", "2025", "2026", "2027")
foreach ($v in $versions) {
    $src = "C:\Program Files\Autodesk\Revit $v"
    $dst = "tools/revit-stubs/$v"
    if (Test-Path $src) {
        New-Item -ItemType Directory -Force -Path $dst
        Copy-Item "$src\RevitAPI.dll" $dst
        Copy-Item "$src\RevitAPIUI.dll" $dst
        Copy-Item "$src\AdWindows.dll" $dst -ErrorAction SilentlyContinue
        Write-Host "Copied stubs for Revit $v" -ForegroundColor Green
    } else {
        Write-Host "Revit $v not found at $src — using interim stub" -ForegroundColor Yellow
    }
}
# Kiểm tra git status — nếu có DLL mới/đổi → commit
git add tools/revit-stubs/
git commit -m "chore: refresh Revit stubs"
```

## License

Autodesk Revit DLL — chỉ commit vào **repo private**. Public repo phải dùng metadata-only stub
generator (JustAssembly / GenAPI). MEPAuto repo hiện tại single-tenant private → chấp nhận.

## Cấu trúc

```
tools/revit-stubs/
├── 2022/
│   ├── RevitAPI.dll
│   ├── RevitAPIUI.dll
│   └── AdWindows.dll
├── 2023/ (tương tự)
├── 2024/ (tương tự)
├── 2025/ (tương tự)
├── 2026/ (tương tự — interim copy từ 2025)
└── 2027/ (tương tự — interim copy từ 2025)
```

DLL chưa được commit vào repo này. LEAD cần chạy script trên để gen stub từ máy LEAD có Revit.
