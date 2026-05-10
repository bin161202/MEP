# Rule 07 — Contract DTOs + IDataStorageService

> **TL;DR**: Wire format (Client ↔ Server) là DTO trong `MEPAuto.Contracts` (.NET Standard 2.0, thuần data). 4 loại DTO chuẩn per feature: `*SnapshotData`, `*Request`, `*Response`, `*ResultRequest`. Cross-feature data đi qua `IDataStorageService` (KHÔNG `<ProjectReference>` giữa feature).

## DTO conventions

### 4 loại DTO per feature

```csharp
// shared/MEPAuto.Contracts/DTOs/{Feature}Dtos.cs
namespace MEPAuto.Contracts.DTOs
{
    // 1. Snapshot — probe data Client gửi server
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

1. **Thuần data**: chỉ property + default constructor. KHÔNG method logic, KHÔNG validation.

2. **KHÔNG reference Revit API + Server.Core/Server.Infrastructure**.

3. **ElementId là string** (xem rule 02). Không leak `int` vs `long`.

4. **Length unit là mm** (xem rule 04). UnitHelper convert nội bộ Client.

5. **Nullable annotation**: `<Nullable>enable</Nullable>`. String default `= ""`, array default `= Array.Empty<T>()`.

6. **Default ctor public** + property setter public: Newtonsoft.Json deserialize cần.

### Element data — PHẢI dùng `List<TypedData>` ⭐

Mọi element trả ra Snapshot/Response/ResultRequest **PHẢI** được nhóm vào `List<XxxData>`:

```csharp
// ✓ ĐÚNG — gom vào typed List
public class DuctRoutingResultRequest
{
    public string JobId { get; set; } = "";
    public bool Success { get; set; }
    public List<DuctData> CreatedDucts { get; set; } = new List<DuctData>();
    public List<FittingData> CreatedFittings { get; set; } = new List<FittingData>();
    public string ErrorMessage { get; set; } = "";
}

// ❌ SAI — ElementId rời
public class DuctRoutingResultRequest
{
    public string MainDuctId { get; set; } = "";
    public string ElbowId { get; set; } = "";
}

// ❌ SAI — List<string> IDs, thiếu properties tái dựng
public class DuctRoutingResultRequest
{
    public List<string> CreatedDuctIds { get; set; } = new();
}
```

### Reuse Data class

| Class | Purpose | File |
|---|---|---|
| `Point3D` | XYZ mm | GeometryDtos.cs |
| `Vector3D` | delta mm | GeometryDtos.cs |
| `LevelData` | Id + Name + ElevationMm | RevitSnapshotData.cs |
| `FamilyTypeData` | FamilyName + TypeName + Id | RevitSnapshotData.cs |
| `ElementSnapshotData` | Id + Category + Location + Parameters | RevitSnapshotData.cs |
| `ParameterValueData` | Name + Value (typed) | RevitSnapshotData.cs |

## IDataStorageService — share data giữa feature

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
ductrouting/jobs/{jobId}
sprinkler/lastplan/{userId}
session/{userId}/{sessionId}
refresh/{tokenHash}
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

## Anti-pattern ❌

❌ **DTO có method logic**:
```csharp
public class DuctRoutingSnapshotData {
    public LevelData GetGroundLevel() => Levels.OrderBy(l => l.ElevationMm).First();  // ❌
}
```

❌ **Feature reference feature khác để đọc data**:
```xml
<ProjectReference Include="..\MEPAuto.Server.Sprinkler\..." />  <!-- ❌ -->
```

❌ **DTO reference Revit API**:
```csharp
using Autodesk.Revit.DB;
public class DuctSnapshotData { public XYZ[] Positions { get; set; } }  // ❌
```

## Reference

- `shared/MEPAuto.Contracts/DTOs/` — DTO files
- `src/server/MEPAuto.Server.Core/Abstractions/IDataStorageService.cs` — interface
- `src/server/MEPAuto.Server.Infrastructure.FileSystem/JsonFileDataStorageService.cs` — Phase 1 impl
