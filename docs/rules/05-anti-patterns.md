# Rule 05 — Anti-patterns: ĐỪNG LÀM

> **TL;DR**: 9 patterns đã gây bug thực tế — đọc trước khi viết feature mới để khỏi lặp lại.

---

## ❌ 1. Gọi `.IntegerValue` / `.Value` trên ElementId direct

**Bug**: code chỉ chạy được trên 1 nhóm runtime, fail compile trên nhóm còn lại. CS0618 warning trên Revit 2024+.

**Fix**: ĐI QUA `ElementIdAdapter.GetValue(id)` / `ElementIdAdapter.Create(value)`.

**Enforce**: CI lint `tools/verify-elementid-usage.ps1`. Xem rule `02-multi-version.md`.

---

## ❌ 2. `[Transaction]` chỉ trên BaseFeatureCommand, không trên class concrete

**Bug**: Revit dialog "Could not run the add-in because it does not have the Transaction attribute assigned" lúc click button.

**Root cause**: `[Transaction]` của Autodesk.Revit.Attributes có `AttributeUsage(Inherited = false)`. Revit kiểm tra trên class concrete.

**Fix**: MỖI feature command phải khai báo lại:
```csharp
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class DuctRoutingCommand : BaseFeatureCommand { ... }
```

---

## ❌ 3. Async không có ConfigureAwait(false) → deadlock

**Bug**: nhập credentials login → Revit treo cứng (frozen), phải kill qua Task Manager.

**Root cause**: `IExternalCommand.Execute` là sync API. Feature command `.GetAwaiter().GetResult()` block UI thread. Nếu library async (ServerProxy) thiếu `.ConfigureAwait(false)` ở MỌI internal `await` → deadlock.

**Fix**: trong code library Client (ServerProxy, JwtCache async, ...) — `.ConfigureAwait(false)` ở **MỌI** `await`.

**Verify**: nếu Revit treo sau click feature → check ConfigureAwait(false) đầu tiên.

---

## ❌ 4. Build feature project, copy DLL cũ từ Client.Shell/bin/

**Bug**: sửa `HelloWorldCommand.cs`, build chỉ project HelloWorld, copy DLL từ `MEPAuto.Client.Shell/bin/Release-2024/` → DLL cũ chưa có thay đổi.

**Root cause**: build 1 project chỉ refresh DLL ở project's own bin. DLL ở `Client.Shell/bin/` là transitive copy lúc Client.Shell build.

**Fix**: Build full sln: `dotnet build MEPAuto.sln -c Release-2024`

---

## ❌ 5. IConfiguration cho secret/key trên Linux container

**Bug**: API container start, tất cả request 500. Log: `IDX10703: key length is zero`. `docker exec env` xác nhận env var có giá trị nhưng `Configuration["Jwt:SigningKey"]` trả empty string.

**Root cause**: ASP.NET Core 8 IConfiguration trong Linux container Docker đôi khi không pick up env var không-prefix.

**Fix**: dùng `EnvOrConfig` helper đọc `Environment.GetEnvironmentVariable` trước, fallback `Configuration[key]`. Xem `Program.cs`. Áp dụng cho mọi secret/key.

---

## ❌ 6. Ghi vào `C:\Program Files\` không có admin

**Bug**: cài MSI hoặc cp manually vào `C:\Program Files\MEPAuto\` báo permission denied.

**Fix**: MSI ship `Scope="perUserOrMachine"`. Lúc cài user chọn:
- **Just me** (không cần admin): DLL → `%LocalAppData%\MEPAuto\{ver}\`
- **All users** (UAC): DLL → `C:\Program Files\MEPAuto\{ver}\`

`<Assembly>` trong `.addin` patch runtime qua `util:XmlFile` (placeholder `__MEPAUTO_INSTALL_PATH__` → path thật).

---

## ❌ 7. Domain logic ở Client

**Bug**: thuật toán "trí tuệ" feature lộ ra binary Client → user reverse engineer được IP.

**Fix**: di chuyển algorithm sang `MEPAuto.Server.{Feature}.Domain`. Client chỉ probe + apply.

---

## ❌ 8. Feature reference feature khác

**Bug**: tight coupling, build order phức tạp, refactor 1 feature break feature khác.

**Fix**: feature share data qua `IDataStorageService`.

KHÔNG `<ProjectReference Include="..\MEPAuto.OtherFeature\MEPAuto.OtherFeature.csproj" />` giữa 2 feature.

---

## ❌ 9. Skip license check trong server endpoint

**Bug**: bất kỳ user đăng nhập đều dùng được mọi feature. License model bị vô hiệu.

**Fix**: MỌI feature endpoint check license inline:
```csharp
[HttpPost("execute")]
public async Task<IActionResult> Execute([FromBody] FeatureRequest req)
{
    if (!await _license.CanUse(User, "feature.basic"))
        return StatusCode(403, new { error = "license_required", feature = "feature.basic" });
    var resp = await _svc.Execute(req, User);
    return Ok(resp);
}
```

---

## Reference đầy đủ

Xem `docs/rules/01-architecture.md` (boundary) + `docs/rules/02-multi-version.md` (ElementId) + `docs/rules/08-external-event-and-callbacks.md` (ExternalEvent deadlock).
