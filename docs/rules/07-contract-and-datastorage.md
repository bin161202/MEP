# Rule 07 — Contract DTOs + IDataStorageService

> **TL;DR**: Wire format (Client ↔ Server) là DTO trong `MEPAuto.Contracts` (.NET Standard 2.0, thuần data). 4 loại DTO chuẩn per feature: `*SnapshotData`, `*Request`, `*Response`, `*ResultRequest`. Cross-feature data đi qua `IDataStorageService` (KHÔNG `<ProjectReference>` giữa feature). Phase 1 JSON file, Phase 2 Redis — swap chỉ ở DI.

## Tại sao có rule này

- **Contracts decouple Client + Server**: 2 bên evolve được, miễn không break wire format.
- **netstandard2.0 cho phép share** giữa net48 (Client 2022-2024) + net8.0-windows (Client 2025-2027) + net8.0 (Server) — KHÔNG conditional compilation.
- **DataStorage interface**: feature share data mà KHÔNG tight coupling. Feature `RouteValidator` cần đọc data feature `DuctRouting` đã tính → đi qua `IDataStorageService`, không reference project khác.

## DTO conventions

### 4 loại DTO per feature

```csharp
// shared/MEPAuto.Contracts/DTOs/{Feature}Dtos.cs
namespace MEPAuto.Contracts.DTOs
{
    // 1. Snapshot — probe data Client gửi server (state Revit cần để tính)
    public class DuctRoutingSnapshotData
    {
        public LevelData[] Levels { get; set; } = System.Array.Empty<LevelData>();
        public string[] SelectedElementIds { get; set; } = System.Array.Empty<string>();
        public double SpacingMm { get; set; }
    }

    // 2. Request — wrap Snapshot + user input
    public class DuctRoutingRequest
    {
        public DuctRoutingSnapshotData Snapshot { get; set; } = new();
        public string DuctTypeName { get; set; } = "";
    }

    // 3. Response — Server trả PlanDto cho Client apply
    public class DuctRoutingResponse
    {
        public string Message { get; set; } = "";
        public string JobId { get; set; } = "";
        public List<DuctSegmentData> Segments { get; set; } = new();
    }

    // 4. ResultRequest — Client báo cáo lại sau khi apply (audit)
    public class DuctRoutingResultRequest
    {
        public string JobId { get; set; } = "";
        public bool Success { get; set; }
        public List<DuctData> CreatedDucts { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }
}
```

### Quy tắc cứng DTO

1. **Thuần data**: chỉ property + default constructor + (optional) ctor tiện lợi. KHÔNG method logic, KHÔNG validation, KHÔNG static factory phức tạp.

2. **KHÔNG reference Revit API + Server.Core/Server.Infrastructure**: `MEPAuto.Contracts.csproj` chỉ cho phép `Newtonsoft.Json`.

3. **ElementId là string** (xem rule 02). Không leak `int` vs `long`.

4. **Length unit là mm** (xem rule 04). UnitHelper convert nội bộ Client.

5. **Nullable annotation**: project có `<Nullable>enable</Nullable>`. String default `= ""`, array default `= Array.Empty<T>()`. Optional dùng `string?` / `T?` rõ ràng.

6. **Default ctor public** + property setter public: Newtonsoft.Json deserialize cần.

### Reuse Data class

Data class chung tách ra `RevitSnapshotData.cs`, `GeometryDtos.cs`, `PlacementData.cs`:

| Class | Purpose | File |
|---|---|---|
| `Point3D` | XYZ mm | GeometryDtos.cs |
| `Vector3D` | delta mm | GeometryDtos.cs |
| `LevelData` | Id + Name + ElevationMm | RevitSnapshotData.cs |
| `FamilyTypeData` | FamilyName + TypeName + Id | RevitSnapshotData.cs |
| `ElementSnapshotData` | Id + Category + Location + Parameters | RevitSnapshotData.cs |
| `ParameterValueData` | Name + Value (typed) | RevitSnapshotData.cs |

Khi feature mới cần data type chưa có → thêm vào file class chung (không tạo riêng `MyFeatureLevelData`).

### Element data — PHẢI dùng `List<TypedData>` ⭐

Mọi element (duct, pipe, fitting, terminal, ...) trả ra Snapshot/Response/ResultRequest **PHẢI** được nhóm vào `List<XxxData>` — KHÔNG trả ElementId rời, KHÔNG trả `List<string>` chỉ chứa IDs.

**Lý do**: thêm property mới (`SystemType`, `InsulationThickness`, `LevelName`, ...) chỉ cần sửa class data — KHÔNG đụng Output DTO hay tất cả caller. Đồng thời đủ chi tiết cho `IDataStorageService` + feature sau tái dựng (xem nguyên lý "xoá hết Revit elements rồi đọc DTO có tái dựng được không").

```csharp
// ✓ ĐÚNG — gom vào typed List, dễ mở rộng
public class DuctRoutingResultRequest
{
    public string JobId { get; set; } = "";
    public bool Success { get; set; }
    public List<DuctData> CreatedDucts { get; set; } = new List<DuctData>();
    public List<FittingData> CreatedFittings { get; set; } = new List<FittingData>();
    public string ErrorMessage { get; set; } = "";
}
```

```csharp
// ❌ SAI — ElementId rời, không scale khi thêm element/property
public class DuctRoutingResultRequest
{
    public string MainDuctId { get; set; } = "";
    public string TeeId { get; set; } = "";
    public string Elbow1Id { get; set; } = "";
    // ... rời rạc, mỗi feature mới phải đập DTO
}

// ❌ SAI — List<string> IDs, thiếu properties tái dựng
public class DuctRoutingResultRequest
{
    public List<string> CreatedDuctIds { get; set; } = new List<string>();
    public List<string> CreatedFittingIds { get; set; } = new List<string>();
    // không biết toạ độ, đường kính, system type → feature sau bó tay
}
```

**Convention `List<T>` vs `T[]`**: ưu tiên `List<T>` cho element data (dễ append, dễ mở rộng). `T[]` chỉ dùng cho geometry-only positions cố định size (vd `Point3D[]` positions).

## IDataStorageService — share data giữa feature

### Khi nào dùng

- Feature B cần đọc output feature A (vd `RouteValidator` đọc plan `DuctRouting` đã tạo).
- Feature persist preference user / session state (ngoài JWT cache).
- Feature cần audit history phục vụ feature khác (vd "last 5 ductrouting jobs").

### Khi KHÔNG dùng

- Audit log thường (đã có `IAuditLogger`).
- User/license data (đã có `IUserRepository`/`ILicenseService`).
- One-shot data trong cùng request (truyền qua method param hoặc DI scope).

### API

```csharp
public interface IDataStorageService
{
    Task<T?> Get<T>(string key) where T : class;
    Task Set<T>(string key, T value) where T : class;
    Task<bool> Delete(string key);
}
```

### Convention key

Format: `{feature-lower}/{entity}/{id}`. Slash → folder Phase 1, key delimiter Phase 2 Redis.

```
ductrouting/jobs/{jobId}                → DuctRoutingResponse cached cho 24h
sprinkler/lastplan/{userId}             → SprinklerPlanData
session/{userId}/{sessionId}            → SessionState
refresh/{tokenHash}                     → RefreshTokenData
```

### Pattern usage trong Service

```csharp
// MEPAuto.Server.DuctRouting.Application
public class DuctRoutingService
{
    private readonly IDataStorageService _storage;
    private readonly IAuditLogger _audit;

    public DuctRoutingService(IDataStorageService storage, IAuditLogger audit)
    { _storage = storage; _audit = audit; }

    public async Task<DuctRoutingResponse> Execute(DuctRoutingRequest req, ClaimsPrincipal user)
    {
        var segments = DuctRoutingLogic.Compute(req.Snapshot, req.DuctTypeName);
        var jobId = Guid.NewGuid().ToString("N");
        var resp = new DuctRoutingResponse
        {
            JobId = jobId,
            Segments = segments,
            Message = $"Computed {segments.Count} duct segments",
        };

        // Persist for cross-feature query
        await _storage.Set($"ductrouting/jobs/{jobId}", resp);
        await _audit.Log(user, "ductrouting.execute", new { jobId, count = segments.Count });
        return resp;
    }
}
```

Feature khác đọc:
```csharp
public class RouteValidatorService
{
    private readonly IDataStorageService _storage;

    public async Task<ValidationResult> Validate(string ductRoutingJobId, ...)
    {
        var ductJob = await _storage.Get<DuctRoutingResponse>($"ductrouting/jobs/{ductRoutingJobId}");
        if (ductJob is null) return ValidationResult.NotFound;
        // ... validate based on ductJob.Segments
    }
}
```

## Phase 1 → Phase 2 swap

Application code KHÔNG đổi. Chỉ DI registration trong `Program.cs`:

```csharp
// Phase 1 (hiện tại)
builder.Services.AddSingleton<IDataStorageService>(sp =>
    new JsonFileDataStorageService("/var/mepauto-data/storage"));

// Phase 2 — swap khi đủ user
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnStr));
builder.Services.AddSingleton<IDataStorageService, RedisDataStorageService>();
```

Migration tool one-shot copy JSON files → Redis keys (xem rule 06).

## Anti-pattern ❌

❌ **DTO có method logic**:
```csharp
public class DuctRoutingSnapshotData {
    public LevelData[] Levels { get; set; } = ...;
    public LevelData GetGroundLevel() => Levels.OrderBy(l => l.ElevationMm).First();  // ❌
}
```
Method này thuộc Domain logic — di sang `Server.DuctRouting.Domain.DuctRoutingLogic`.

❌ **Feature reference feature khác để đọc data**:
```xml
<!-- MEPAuto.Server.RouteValidator.csproj -->
<ProjectReference Include="..\MEPAuto.Server.DuctRouting\MEPAuto.Server.DuctRouting.csproj" />  <!-- ❌ -->
```
Phải qua `IDataStorageService` + DTO trong `Contracts`.

❌ **Hardcode storage path**:
```csharp
public class DuctRoutingService {
    public void Save(DuctRoutingResponse resp) {
        File.WriteAllText($"/var/mepauto-data/ductrouting/{resp.JobId}.json",
            JsonConvert.SerializeObject(resp));  // ❌
    }
}
```
Phải `_storage.Set($"ductrouting/jobs/{resp.JobId}", resp)`.

❌ **DTO reference Revit API**:
```csharp
// MEPAuto.Contracts/DTOs/DuctRoutingDtos.cs
using Autodesk.Revit.DB;  // ❌ Contracts không reference Revit
public class DuctRoutingSnapshotData {
    public XYZ[] Positions { get; set; }  // ❌ — phải Point3D
}
```

❌ **Element trả ra dưới dạng ID rời / `List<string>`**:
```csharp
public class DuctRoutingResultRequest {
    public string MainDuctId { get; set; } = "";              // ❌ rời
    public List<string> BranchDuctIds { get; set; } = new();  // ❌ thiếu data
}
```
Phải gom vào `List<DuctData>` / `List<FittingData>` (xem "Element data — PHẢI dùng `List<TypedData>`").

## Reference

- `shared/MEPAuto.Contracts/DTOs/` — DTO files
- `shared/MEPAuto.Contracts/DTOs/GeometryDtos.cs` — Point3D, Vector3D
- `shared/MEPAuto.Contracts/DTOs/RevitSnapshotData.cs` — Level, FamilyType, Element
- `src/server/MEPAuto.Server.Core/Abstractions/IDataStorageService.cs` — interface
- `src/server/MEPAuto.Server.Infrastructure.FileSystem/JsonFileDataStorageService.cs` — Phase 1 impl