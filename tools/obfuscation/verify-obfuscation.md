# Verify obfuscation — checklist sau khi build MSI có ConfuserEx

Sau khi `Build-MSI.ps1` chạy xong (qua `release.yml` hoặc local), LEAD chạy checklist này để xác nhận obfuscation thực sự work + không break Revit reflection.

## 1. Decompile spot-check (3 phút)

Cài [dnSpyEx](https://github.com/dnSpyEx/dnSpy/releases) (nhánh community fork dnSpy đã chết).

1. Cài MSI lên máy có Revit 2024.
2. Mở dnSpy → File → Open → `%LocalAppData%\MEPAuto\2024\MEPAuto.Client.Shell.dll`
3. Verify CÓ obfuscation:
   - Class names có dạng `a`, `b`, `` thay vì `LoginCommand`, `HelloWorldCommand`
   - Method names cũng vậy
   - String literal `https://api.MEPAuto...` KHÔNG xuất hiện trong tree (đã encrypt qua `constants` protection)
   - Control flow trong method body có goto/switch lộn xộn
4. Verify KHÔNG đụng:
   - Class `MEPAuto.Client.Shell.RevitApp` vẫn giữ nguyên tên (whitelist IExternalApplication)
   - Class `MEPAuto.HelloWorld.HelloWorldCommand` (hoặc tương tự) vẫn giữ tên (whitelist IExternalCommand)
   - DTO trong `MEPAuto.Contracts` namespace giữ nguyên tên field

## 2. Strings dump check

```powershell
# Cài Sysinternals strings.exe trước (https://learn.microsoft.com/sysinternals/downloads/strings)
strings64.exe -a "$env:LocalAppData\MEPAuto\2024\MEPAuto.Client.Shell.dll" | Select-String -Pattern "MEPAuto|login|password|jwt|secret"
```

**Pass**: KHÔNG match string nhạy cảm (URL VPS, "password", v.v.).
**Fail**: thấy `https://api.MEPAuto...` raw → constants protection chưa apply.

## 3. Functional smoke test trong Revit

Đây là test quan trọng nhất — obfuscation hay break reflection.

1. Mở Revit 2024 → tab **MEPAuto** xuất hiện.
2. Click button HelloWorld → command thực hiện thành công, gọi VPS, hiện TaskDialog response.
3. Click button DrainageBranchRouting → flow đầy đủ probe → execute → result KHÔNG crash với `MissingMethodException` hoặc `TypeLoadException`.
4. Đóng Revit → mở lại → ribbon vẫn build (không Stale `IExternalApplication` reference).

Nếu Revit báo không tìm thấy class trong manifest:
- Mở `%AppData%\Autodesk\Revit\Addins\2024\MEPAuto-2024.addin`
- Check `<FullClassName>MEPAuto.Client.Shell.RevitApp</FullClassName>` → phải khớp với class trong DLL
- Mở dnSpy → tìm class `RevitApp` trong namespace `MEPAuto.Client.Shell` → phải còn

## 4. Multi-version (khuyến nghị spot 2 version)

Test ít nhất 1 version net48 (2024) + 1 version net8 (2025 nếu có Revit cài).

**Lưu ý quan trọng** (theo plan): ConfuserEx 2 mkaring fork chưa support .NET 8 ổn định. Nếu Revit 2025/2026/2027 fail:
- DLL ship un-obfuscated (script log warning)
- Functional test vẫn pass (vì DLL gốc, không break)
- Future fix: chuyển sang Eziriz hoặc Babel (commercial) hoặc skip obfuscate cho net8.

## 5. CI verify checklist (LEAD review log)

Trong workflow `Release` GitHub Actions, log của step "Obfuscate" cần thấy:
- Download ConfuserEx zip thành công lần đầu (cache lần sau)
- Mỗi version: `Done Release-XXXX (N DLL)`
- Cuối: `DONE — Obfuscation pass cho tất cả 6 version` HOẶC warning specific version fail
- KHÔNG throw exception (fail per-version chỉ log, không stop pipeline)

Nếu thấy `Confuser.CLI.exe không tìm thấy`: check URL release còn live (mkaring có thể đổi tag).

---

## Khi obfuscation break add-in (rollback fast)

1. Local: chạy `Build-MSI.ps1 -SkipObfuscation` (flag thêm ở Phase 4 step 3) để build MSI raw.
2. CI: comment-out step "Obfuscate" trong `release.yml`, push lại tag → MSI mới không obfuscate.
3. Investigate: tăng whitelist trong `MEPAuto.Client.crproj` cho class hoặc method bị Revit reflect mà chưa được protect.
