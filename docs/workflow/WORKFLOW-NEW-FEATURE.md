# Workflow — Tạo feature mới (12-step checklist)

Cẩm nang member theo từ A-Z khi nhận task feature mới.

## Tiền điều kiện

- ✅ Đọc `docs/rules/01-architecture.md`
- ✅ Đọc `docs/rules/04-irevitservice.md`
- ✅ Đọc `docs/rules/05-anti-patterns.md`
- ✅ Đọc `docs/rules/07-contract-and-datastorage.md`
- ✅ Đọc `docs/rules/09-feature-project-structure.md`
- ✅ Build pass `dotnet build MEPAuto.sln -c Release-2024`
- ✅ Có account user test + access VPS để cấp license

## 12 step

### 1. Quick design (5 phút)

Trả lời 4 câu hỏi:
1. **Input**: user select gì? UI dialog cần fields nào?
2. **Probe**: cần data gì từ Revit (levels, family types, selected elements)?
3. **Domain logic**: thuật toán server tính gì? Output là gì?
4. **Apply**: client làm gì với output (CreateDuct, SetParameter, ...)?

### 2. Tạo skeleton

```
MEPAuto/
├── src/client/features/MEPAuto.{Name}/
│   ├── MEPAuto.{Name}.csproj
│   ├── Manifest/{Name}Manifest.cs
│   ├── Commands/{Name}Command.cs
│   └── Contracts/{Name}Contract.cs
└── src/server/features/MEPAuto.Server.{Name}/
    ├── MEPAuto.Server.{Name}.csproj
    ├── Domain/{Name}Logic.cs
    ├── Application/{Name}Service.cs
    └── Endpoint/{Name}Controller.cs
```

Thêm vào `MEPAuto.sln`, `MEPAuto.Client.Shell.csproj`, `MEPAuto.Server.Api/Program.cs`.

### 3. Sketch DTO

Edit `shared/MEPAuto.Contracts/DTOs/{Name}Dtos.cs`:

```csharp
public class MyFeatureSnapshotDto
{
    public string LevelName { get; set; } = "";
    public string[] SelectedElementIds { get; set; } = System.Array.Empty<string>();
}

public class MyFeatureRequest
{
    public MyFeatureSnapshotDto Snapshot { get; set; } = new();
}

public class MyFeatureResponse
{
    public string Message { get; set; } = "";
    public string JobId { get; set; } = "";
}
```

### 4. Implement Domain logic

```csharp
public static class MyFeatureLogic
{
    public static ChangeDto[] Compute(MyFeatureSnapshotDto snapshot, string pattern)
    {
        // Pure function — no DI, no IO, no Revit API
        return snapshot.SelectedElementIds
            .Select((id, i) => new ChangeDto { Id = id, NewValue = string.Format(pattern, i) })
            .ToArray();
    }
}
```

### 5. Implement Application Service

```csharp
public class MyFeatureService
{
    private readonly IAuditLogger _audit;
    public MyFeatureService(IAuditLogger audit) { _audit = audit; }

    public async Task<MyFeatureResponse> Execute(MyFeatureRequest req, ClaimsPrincipal user)
    {
        var changes = MyFeatureLogic.Compute(req.Snapshot, "EQ-{0:D3}");
        var jobId = Guid.NewGuid().ToString("N");
        await _audit.Log(user, "myfeature.execute", new { jobId, count = changes.Length });
        return new MyFeatureResponse { Message = $"Computed {changes.Length} changes", JobId = jobId };
    }
}
```

### 6. Implement Client Command (3 method tách bạch)

```csharp
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class MyFeatureCommand : BaseFeatureCommand
{
    protected override Result RunFeature(IFeatureContext ctx)
    {
        var input = BuildInput(ctx);
        if (input == null) return Result.Cancelled;
        var output = ExecuteHeadless(ctx, input);
        ShowResult(output);
        return Result.Succeeded;
    }

    public static MyFeatureResponse ExecuteHeadless(IFeatureContext ctx, MyFeatureRequest input)
    {
        var output = ctx.Server.Post<MyFeatureResponse>(
            "/api/v1/myfeature/execute", input).GetAwaiter().GetResult();

        ctx.RevitSvc.RunInTransaction("MyFeature", () => {
            foreach (var change in output.Changes)
                ctx.RevitSvc.SetParameter(change.Id, "Mark", change.NewValue);
        });

        return output;
    }

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
                SelectedElementIds = selected.Select(e => e.Id).ToArray(),
            },
        };
    }

    private static void ShowResult(MyFeatureResponse output)
        => TaskDialog.Show("MEPAuto - My Feature", output.Message);
}
```

#### 6.2 Contract class (HEADLESS)

```csharp
public class MyFeatureContract : IFeatureContract
{
    public string FeatureName => "MyFeature";               // PHẢI khớp Manifest.Name
    public Type InputType => typeof(MyFeatureRequest);

    public object Execute(IFeatureContext ctx, object input)
    {
        if (input is not MyFeatureRequest req)
            throw new ArgumentException($"Expected MyFeatureRequest, got {input?.GetType().Name}", nameof(input));
        return MyFeatureCommand.ExecuteHeadless(ctx, req);
    }
}
```

### 7. Build sln verify

```bash
dotnet build MEPAuto.sln -c Release-2024 --nologo
```

Mong đợi: `0 Warning(s), 0 Error(s)`.

### 8. Cấp license cho user test

```bash
ssh root@<vps-ip>
nano /var/mepauto-data/licenses.json
# Thêm "myfeature.basic" vào array của user email
docker compose -f /opt/mepauto/tools/deploy/docker-compose.system-nginx.yml restart api
```

### 9. Cài DLL local

```powershell
$src = "d:\MEP Add-in\MEP\MEPAuto\src\client\features\MEPAuto.MyFeature\bin\Release-2024\MEPAuto.MyFeature.dll"
$dst = "$env:LocalAppData\MEPAuto\2024\MEPAuto.MyFeature.dll"
Copy-Item $src $dst -Force
```

### 10. Test thủ công 1 case

Mở Revit 2024 → tab "MEPAuto" → panel → click button → kiểm tra:
- LoginDialog hiện (nếu chưa login)
- Behavior đúng theo design
- Audit log: `ssh root@<vps-ip> 'tail /var/mepauto-data/audit.log'` → entry `myfeature.execute`

### 11. Commit + PR

```bash
cd "d:/MEP Add-in/MEP/MEPAuto"
git add src/client/features/MEPAuto.MyFeature \
        src/server/features/MEPAuto.Server.MyFeature \
        shared/MEPAuto.Contracts/DTOs/MyFeatureDtos.cs \
        MEPAuto.sln \
        src/client/MEPAuto.Client.Shell/MEPAuto.Client.Shell.csproj \
        src/server/MEPAuto.Server.Api/MEPAuto.Server.Api.csproj \
        src/server/MEPAuto.Server.Api/Program.cs
git commit -m "feat: add MyFeature feature"
git push origin feature/myfeature
```

### 12. LEAD review checklist

- ✅ Có Client + Server project
- ✅ Manifest đủ 8 property
- ✅ Có `Contracts/{Name}Contract.cs` với `FeatureName` khớp `Manifest.Name`
- ✅ `[Transaction]` direct trên Command concrete
- ✅ License check inline ở Controller
- ✅ Domain logic ở Server (không Client)
- ✅ Build pass cả 2024 + 2025
