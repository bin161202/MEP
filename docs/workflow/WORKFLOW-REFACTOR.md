# Workflow — Refactor add-in cũ vào MEPAuto monorepo

Cẩm nang khi LEAD/member nhận yêu cầu chuyển 1 add-in Revit cũ (đã có user thật) vào MEPAuto monorepo.

## Khi nào dùng

- Add-in cũ là 1 .NET Framework project standalone, ship qua MSI riêng
- Có user đang dùng → behavior PHẢI khớp y hệt sau refactor
- IP cần protect → di Domain logic sang server-side
- Cần tích hợp license + audit chung với MEPAuto

## Strategy chung

1. **Identify boundary**: probe/apply (Client) vs Domain logic (Server)
2. **Tạo skeleton** MEPAuto-style (gồm `Contracts/{Name}Contract.cs`)
3. **Move Domain code** sang Server, tách dependencies Revit
4. **Wrap probe/apply** qua `IRevitService`
5. **Verify Contract** — `ExecuteHeadless` có logic chính, Contract wrap
6. **Test behavior** verify y hệt add-in cũ
7. **Migrate user data** (nếu có local config cũ)
8. **Soft launch** với 1-2 user test
9. **Sunset add-in cũ**

## Bước chi tiết

### 1. Phân loại code add-in cũ

| Nhóm | Đặc điểm | Đi đâu |
|---|---|---|
| **A. Probe** | `FilteredElementCollector`, `Document.GetElement` | Client `RevitService.Get*()` |
| **B. Apply** | `Pipe.Create`, `Element.SetParameter` | Client `RevitService.Create*()` / `SetParameter()` |
| **C. Domain** | Algorithm, validation, business rule (no Revit API) | Server `Domain/{Feature}Logic.cs` |
| **D. UI** | WPF Window, TaskDialog | Client (có thể giữ structure cũ) |

### 2. Tạo skeleton MEPAuto-style

Xem `docs/workflow/WORKFLOW-NEW-FEATURE.md` step 2.

### 3. Move Domain logic sang Server

Vd add-in cũ có code tính toán:
```csharp
// File cũ — references Revit API (XYZ, Level, ...)
public static class SprinklerCalculator {
    public static List<XYZ> Compute(Document doc, double spacingMm, ...) { ... }
}
```

Refactor sang server:
```csharp
// File mới — pure function
namespace MEPAuto.Server.Sprinkler.Domain;
public static class SprinklerSpacingLogic {
    public static Point3D[] Compute(SprinklerSnapshotData snapshot, double spacingMm) {
        // Thuật toán giữ nguyên, chỉ thay XYZ → Point3D (mm thay vì feet)
    }
}
```

Convert:
- `XYZ` (Revit, feet) → `Point3D` (Contracts DTO, **mm**)
- `Level` → `LevelData` (Id + Name + ElevationMm)
- `FamilyInstance` → `ElementSnapshotData`

### 4. Wrap probe/apply qua IRevitService

```csharp
// Probe before
var levels = ctx.RevitSvc.GetLevels();
var familyTypes = ctx.RevitSvc.GetFamilyTypes(BuiltInCategory.OST_Sprinklers);

// POST server với snapshot
var plan = ctx.Server.Post<SprinklerResponse>("/api/v1/sprinkler/execute",
    new SprinklerRequest { Snapshot = new() { Levels = levels } })
    .GetAwaiter().GetResult();

// Apply qua interface
ctx.RevitSvc.RunInTransaction("Place sprinklers", () => {
    foreach (var pos in plan.Positions)
        ctx.RevitSvc.CreateFamilyInstance("Sprinkler_Standard", "DN15", pos, plan.LevelName);
});
```

Nếu code cũ dùng Revit API chưa có trong `IRevitService` → thêm method mới (rule 04).

### 5. Verify Contract layer

Kiểm tra:
- Logic chính nằm trong `Command.ExecuteHeadless` — KHÔNG ở `RunFeature`
- `Contract.InputType` khớp DTO `ExecuteHeadless` nhận
- `Contract.FeatureName` khớp `Manifest.Name`

### 6. Test behavior

Tối thiểu 3 scenario:
1. Happy path: output element count + parameter giống add-in cũ
2. Edge case: input boundary (empty selection, ...) → cùng error message
3. Rollback: input invalid → cùng RollBack state

### 7. Migrate user data

- Config cũ: `%AppData%\OldAddin\config.json` → copy `%LocalAppData%\MEPAuto\config.json` (ServerBaseUrl)
- License: cấp `feature.basic` cho all user existing

### 8. Soft launch + Sunset

- Cài MSI MEPAuto cho 1-2 power user
- Monitor: `ssh root@<vps> 'tail -f /var/mepauto-data/audit.log'`
- 1 tuần → collect feedback → fix regression
- Email user migrate → add-in cũ thông báo "Vui lòng cài MEPAuto"
- Sau 4 tuần → uninstall MSI cũ

## Anti-pattern khi refactor ❌

❌ **Copy nguyên Domain logic vào Client mới**: IP vẫn lộ.

❌ **Gọi Revit API direct vì "code cũ làm vậy"**: phải refactor qua `IRevitService`.

❌ **Skip regression test**: bug regression mất uy tín.

❌ **Đổi behavior tận dụng cơ hội refactor**: scope creep. Refactor first → ship → improve sau.

❌ **Để logic chính trong `RunFeature` thay vì `ExecuteHeadless`**: AI/CAD-PDF mode không gọi được.

## Reference

- `docs/workflow/WORKFLOW-NEW-FEATURE.md`
- `docs/rules/01-architecture.md`
- `docs/rules/04-irevitservice.md`
