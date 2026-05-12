# Workflow — Tạo feature mới (12-step checklist)

Cẩm nang member theo từ A-Z khi nhận task feature mới.

## Tiền điều kiện

- ✅ Đọc `docs/rules/01-architecture.md` (Client thin + Server full + IFeatureContract)
- ✅ Đọc `docs/rules/04-irevitservice.md` (interface + đơn vị mm + pick connector)
- ✅ Đọc `docs/rules/05-anti-patterns.md` (patterns tránh)
- ✅ Đọc `docs/rules/07-contract-and-datastorage.md` (wire DTO conventions)
- ✅ Đọc `docs/rules/09-feature-project-structure.md` (cấu trúc folder + naming)
- ✅ Build pass `dotnet build MEPAuto.sln -c Release-2024`
- ✅ Có account user test + access VPS để cấp license

## 12 step

### 1. Quick design (5 phút)

Trả lời 4 câu hỏi vào file scratch (không commit):
1. **Input**: user select gì? UI dialog cần fields nào?
2. **Probe**: cần data gì từ Revit (levels, family types, selected elements, ...)?
3. **Domain logic**: thuật toán server tính gì? Output là gì (positions, renames, ...)?
4. **Apply**: client làm gì với output (CreateDuct, CreateFamilyInstance, SetParameter, ...)?

Nếu không trả lời được → cần làm rõ requirement với LEAD trước khi code.

### 2. Run new-feature.ps1 sinh skeleton

```powershell
cd "d:\MEP Add-in\MEP\MEPAuto"
powershell -ExecutionPolicy Bypass -File tools/add-feature/new-feature.ps1 -Name MyFeature -Panel "MEPAuto - HVAC" -Order 20
```

Nếu feature cần **WPF dialog** input (form nhập pattern, picker level, v.v.) → thêm `-WithUi`:

```powershell
powershell -ExecutionPolicy Bypass -File tools/add-feature/new-feature.ps1 -Name MyFeature -Panel "MEPAuto - HVAC" -Order 20 -WithUi
```

Script tự:
- Sinh file template:
  - Client: `Manifest/{Name}Manifest.cs`, `Commands/{Name}Command.cs`, `Contracts/{Name}Contract.cs`, `MEPAuto.{Name}.csproj`
  - Server: `Domain/{Name}Logic.cs`, `Application/{Name}Service.cs`, `Endpoint/{Name}Controller.cs`, `MEPAuto.Server.{Name}.csproj`
  - Wire DTO: `shared/MEPAuto.Contracts/DTOs/{Name}Dtos.cs`
- Nếu `-WithUi`: thêm 3 file UI — `Views/{Name}Window.xaml(.cs)` + `ViewModels/{Name}WindowViewModel.cs`, Command đã wire sẵn ShowDialog
- Add 2 project vào sln + ProjectReference vào Client.Shell + Server.Api
- Insert `AddScoped<MyFeatureService>` trong Program.cs
- Build verify pass

Convention naming:
- `Name`: PascalCase, vd `DuctRouting`, `RenameElements`
- `Panel`: format `MEPAuto - {Category}` (xem `RIBBON-CONVENTIONS.md`)
- `Order`: số 1-99 theo thứ tự button trong panel

### 3. Sketch DTO

Edit `shared/MEPAuto.Contracts/DTOs/{Name}Dtos.cs`:

```csharp
public class MyFeatureSnapshotDto
{
    // Probe data Client gửi server — Level/element/parameter relevant
    public string LevelName { get; set; } = "";
    public string[] SelectedElementIds { get; set; } = System.Array.Empty<string>();
}

public class MyFeatureRequest
{
    public MyFeatureSnapshotDto Snapshot { get; set; } = new();
    public string Pattern { get; set; } = "";  // user input
}

public class MyFeatureResponse
{
    public string Message { get; set; } = "";
    public string JobId { get; set; } = "";
    public ChangeDto[] Changes { get; set; } = System.Array.Empty<ChangeDto>();  // PlanDto Client apply
}
```

Tip: bắt đầu nhỏ, refine khi implement Domain + Command.

### 4. Implement Domain logic

Edit `src/server/features/MEPAuto.Server.{Name}/Domain/{Name}Logic.cs`:

```csharp
public static class MyFeatureLogic
{
    public static ChangeDto[] Compute(MyFeatureSnapshotDto snapshot, string pattern)
    {
        // Pure function — no DI, no IO
        // Algorithm chính, IP nặng nhất feature
        return snapshot.SelectedElementIds
            .Select((id, i) => new ChangeDto { Id = id, NewValue = string.Format(pattern, i) })
            .ToArray();
    }
}
```

Nguyên tắc:
- **Pure function**: input → output, không phụ thuộc DI / file / DB / network
- **Server-only**: không expose ra Client (IP)
- **Unit test được**: có thể test bằng xUnit không cần Revit

### 5. Implement Application Service

Edit `src/server/features/MEPAuto.Server.{Name}/Application/{Name}Service.cs`:

```csharp
public class MyFeatureService
{
    private readonly IAuditLogger _audit;
    public MyFeatureService(IAuditLogger audit) { _audit = audit; }

    public async Task<MyFeatureResponse> Execute(MyFeatureRequest req, ClaimsPrincipal user)
    {
        var changes = MyFeatureLogic.Compute(req.Snapshot, req.Pattern);
        var jobId = Guid.NewGuid().ToString("N");
        await _audit.Log(user, "myfeature.execute", new { jobId, count = changes.Length });
        return new MyFeatureResponse {
            Message = $"Computed {changes.Length} changes",
            JobId = jobId,
            Changes = changes,
        };
    }

    public async Task RecordResult(MyFeatureResultRequest req, ClaimsPrincipal user)
    {
        await _audit.Log(user, "myfeature.result", new { req.JobId, req.Success },
            req.Success ? "ok" : "fail");
    }
}
```

License đã check ở controller — service KHÔNG check lại.

### 6. Implement Client Command (3 method tách bạch)

Edit `src/client/features/MEPAuto.{Name}/Commands/{Name}Command.cs`. Template mới chia 3 method
để chuẩn bị 3 chế độ User/AI/CAD-PDF — KHÔNG trộn UI vào logic chính.

```csharp
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class MyFeatureCommand : BaseFeatureCommand
{
    // Cửa cho USER bấm ribbon
    protected override Result RunFeature(IFeatureContext ctx)
    {
        var input = BuildInput(ctx);
        if (input == null) return Result.Cancelled;
        var output = ExecuteHeadless(ctx, input);
        ShowResult(output);
        return Result.Succeeded;
    }

    // Cửa HEADLESS — luồng nền (AI/CAD-PDF mode) gọi qua IFeatureContract
    public static MyFeatureResponse ExecuteHeadless(IFeatureContext ctx, MyFeatureRequest input)
    {
        var output = ctx.Server.Post<MyFeatureResponse>(
            "/api/v1/myfeature/execute", input).GetAwaiter().GetResult();

        ctx.RevitSvc.RunInTransaction("MyFeature", () => {
            foreach (var change in output.Changes)
                ctx.RevitSvc.SetParameter(change.Id, "Mark", change.NewValue);
        });

        ctx.Server.Post("/api/v1/myfeature/result",
            new MyFeatureResultRequest { JobId = output.JobId, Success = true })
            .GetAwaiter().GetResult();

        return output;
    }

    // Helper riêng User mode
    private static MyFeatureRequest? BuildInput(IFeatureContext ctx)
    {
        var selected = ctx.RevitSvc.GetSelected();
        if (selected.Length == 0)
        {
            TaskDialog.Show("MEPAuto", "Cần chọn ít nhất 1 element.");
            return null;
        }
        return new MyFeatureRequest {
            Snapshot = new MyFeatureSnapshotDto {
                LevelName = "Level 1",
                SelectedElementIds = selected.Select(e => e.Id).ToArray(),
            },
            Pattern = "EQ-{0:D3}",
        };
    }

    private static void ShowResult(MyFeatureResponse output)
        => TaskDialog.Show("MEPAuto - My Feature", output.Message);
}
```

⚠️ Lưu ý (xem rule 05 anti-patterns):
- `[Transaction]` direct trên class concrete (KHÔNG inherit từ Base)
- `.GetAwaiter().GetResult()` OK vì ServerProxy đã ConfigureAwait(false) bên trong
- `ExecuteHeadless` KHÔNG gọi `TaskDialog.Show` / `PickObject` — luồng nền không show được + chế độ AI không có user pick

#### 6.1 Khi feature cần WPF dialog (`-WithUi`)

Nếu chạy script với `-WithUi`, Command đã sinh sẵn flow:

1. Tạo `{Name}WindowViewModel` + `{Name}Window`, set `Owner = ctx.UiApp.MainWindowHandle`.
2. `ShowDialog() != true` → `Result.Cancelled` (KHÔNG gọi server).
3. Lấy state từ VM → fill vào `{Name}Request` → POST.

Member chỉ cần edit:
- `Views/{Name}Window.xaml`: thêm/đổi field UI (TextBox, ComboBox, ...) — bind `{Binding PropertyName}`.
- `ViewModels/{Name}WindowViewModel.cs`: thêm property INotifyPropertyChanged tương ứng + cập nhật `Validate(out error)`.
- `Commands/{Name}Command.cs`: map `vm.X` → `Request.X` ở chỗ TODO trong block POST.

Quy tắc cứng (rule 09 §7):
- KHÔNG gọi RevitAPI / ServerProxy trong code-behind hay VM.
- Mọi network + Revit call ở Command, sau `ShowDialog() == true`.
- VM giữ state + Validate; View chỉ wire OK → DialogResult.

#### 6.2 Contract class (HEADLESS — chuẩn bị cho AI/CAD-PDF mode)

Script `new-feature.ps1` đã sinh sẵn `src/client/features/MEPAuto.{Name}/Contracts/{Name}Contract.cs` từ template — bạn KHÔNG cần viết tay, chỉ verify nội dung khớp:

```csharp
public class MyFeatureContract : IFeatureContract
{
    public string FeatureName => "MyFeature";                  // PHẢI khớp Manifest.Name
    public Type InputType => typeof(MyFeatureRequest);

    public object Execute(IFeatureContext ctx, object input)
    {
        if (input is not MyFeatureRequest req)
            throw new ArgumentException(
                $"MyFeatureContract.Execute expected MyFeatureRequest, got {input?.GetType().Name ?? "null"}",
                nameof(input));
        return MyFeatureCommand.ExecuteHeadless(ctx, req);
    }
}
```

Quy tắc:
- Chỉ wrap `Command.ExecuteHeadless` — KHÔNG duplicate logic.
- `FeatureName` PHẢI khớp với `Manifest.Name` (dùng làm key trong `IContractRegistry`).
- KHÔNG show dialog, KHÔNG `PickObject` — luồng nền không làm được.
- Khi feature cần input phức tạp (User mode dùng dialog/pick): AI mode tự build DTO trực tiếp khi gửi step xuống.
- KHI ĐỔI signature `ExecuteHeadless` (input/output DTO type) → SYNC `Contract.InputType` + cast trong `Execute`. Quên = `ServerStepHandler` deserialize sai type → runtime ArgumentException.

`ContractRegistry` ở `Client.Shell` tự reflection scan, nhặt class này khi DLL có mặt — KHÔNG cần đăng ký thủ công.

### 7. Build sln verify

```bash
dotnet build MEPAuto.sln -c Release-2024 --nologo
```

Mong đợi: `0 Warning(s), 0 Error(s)`. Nếu warning CS0618 → bạn vô tình gọi `.IntegerValue`/`.Value` direct (rule 02).

### 8. Cấp license cho user test (1 lần per feature)

```bash
ssh root@<vps-ip>
# Edit /var/mepauto-data/licenses.json — thêm "myfeature.basic" vào array của user email
nano /var/mepauto-data/licenses.json
docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml restart api
```

License feature key = `{name-lowercase}.basic` (vd `myfeature.basic`).

### 9. Cài DLL local

**Cách A — manual cp (đầu tiên hoặc khi đổi Manifest)**:
```powershell
$src = "d:\MEP Add-in\MEP\MEPAuto\src\client\features\MEPAuto.MyFeature\bin\Release-2024\MEPAuto.MyFeature.dll"
$dst = "$env:LocalAppData\MEPAuto\2024\MEPAuto.MyFeature.dll"
Copy-Item $src $dst -Force
```

**Cách B — RevitAddinManager Reload (sau cài DLL lần đầu)**: build sln, mở Revit → Add-Ins tab → Add-In Manager → Reload `MEPAuto.MyFeature.dll`.

### 10. Test thủ công 1 case

Mở Revit 2024 → tab "MEPAuto" → panel của feature → click button → kiểm tra:
- LoginDialog hiện (nếu chưa login): nhập creds → JWT cache
- Behavior đúng theo design step 1
- Audit log VPS: `ssh root@<vps-ip> 'tail /var/mepauto-data/audit.log'` thấy entry `myfeature.execute`

### 11. (Optional) MCP auto-test multiple scenarios

Nếu có MCP `send_code_to_revit` + Antigravity wired, hỏi với template prompt từ `tools/dev-setup/test-prompts.md`:
> Auto test feature MyFeature với 3 scenario: A (5 selected), B (0 selected → cancel), C (invalid pattern → error). Theo Prompt 1 probe-invoke-probe.

Antigravity gửi qua MCP `send_code_to_revit`, báo cáo PASS/FAIL từng case.

### 12. Commit + PR

```bash
cd "d:/MEP Add-in/MEP/MEPAuto"
git add src/client/features/MEPAuto.MyFeature \
        src/server/features/MEPAuto.Server.MyFeature \
        shared/MEPAuto.Contracts/DTOs/MyFeatureDtos.cs \
        MEPAuto.sln \
        src/client/MEPAuto.Client.Shell/MEPAuto.Client.Shell.csproj \
        src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj \
        src/server/MEPAuto.Server.Api/Program.cs
# Note: Contracts/{Name}Contract.cs nằm bên trong feature folder → đã catch bằng folder add ở trên.
git commit -m "feat: add MyFeature feature

- Domain: MyFeatureLogic.Compute pattern-based naming
- Application: MyFeatureService.Execute + RecordResult
- Client: MyFeatureCommand probe selected → POST → apply Mark parameter
- License: myfeature.basic"
git push origin feature/myfeature
gh pr create --title "feat: MyFeature" --body "..."
```

LEAD review checklist (xem `WORKFLOW-REFACTOR.md` cho add-in cũ):
- ✅ Có Client + Server project
- ✅ Manifest đủ 8 property (Name, DisplayName, ServerEndpoint, LicenseFeature, PanelGroup, Order, IconResourcePath, CommandType)
- ✅ Có `Contracts/{Name}Contract.cs` với `FeatureName` khớp `Manifest.Name`
- ✅ `Contract.InputType` khớp signature `ExecuteHeadless`
- ✅ `[Transaction]` direct trên Command
- ✅ License check inline ở Controller
- ✅ Domain logic ở Server (không Client)
- ✅ Build pass cả 2024 + 2025

## Khi step nào fail

Xem `MEMBER-DEV-WORKFLOW.md` section Troubleshoot. Search `docs/rules/05-anti-patterns.md` cho symptom tương tự.