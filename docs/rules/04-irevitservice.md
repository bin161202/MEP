# Rule 04 — IRevitService interface

> **TL;DR**: Mọi tương tác Revit qua `IRevitService`. KHÔNG gọi `Document.GetElement(...)`, `Transaction(...)`, `Pipe.Create(...)` direct. 2 implementation: `RevitService` (real, wrap UIDocument) + `FakeRevitService` (test, không cần Revit).

## Tại sao có rule này

- **Test được**: `FakeRevitService` cho phép unit test Command flow không cần load Revit. xUnit chạy trong CI Linux.
- **Type-safe**: interface C# có IntelliSense, compile-time check.
- **Multi-version isolation**: implementation đóng gói `ElementIdAdapter` + Revit API differences — feature code không cần biết Revit 2024 vs 2025.
- **Mockable**: `Moq.Mock<IRevitService>` cho integration test scenarios cụ thể.

## Interface contract

`src/client/MEPAuto.Client.Common/Revit/IRevitService.cs`:

```csharp
public interface IRevitService
{
    // PROBE — read-only, trả typed Data class
    LevelData[] GetLevels();
    FamilyTypeData[] GetFamilyTypes(BuiltInCategory category);
    ElementSnapshotData[] GetByCategory(BuiltInCategory category);
    ElementSnapshotData[] GetSelected();
    ElementSnapshotData? GetById(string elementId);
    ParameterValueData? GetParameter(string elementId, string paramName);

    // CREATE — return ElementId as string
    string CreatePipe(Point3D startMm, Point3D endMm, double dnMm, string systemType,
                      string levelName, string pipeTypeName = "");
    string CreateDuct(Point3D startMm, Point3D endMm, double widthMm, double heightMm, string systemType, string levelName);
    string CreateFamilyInstance(string familyName, string typeName, Point3D posMm, string levelName);
    string CreateFitting(string familyName, Point3D posMm, double sizeMm);

    // MODIFY
    void SetParameter(string elementId, string paramName, object value);
    void MoveElement(string elementId, Vector3D deltaMm);
    void DeleteElement(string elementId);

    // CONNECT (MEP — generic)
    void ConnectConnectors(string elementIdA, int connectorIdxA, string elementIdB, int connectorIdxB);

    // PIPE GEOMETRY (Batch 2)
    string BreakPipe(string pipeId, Point3D atMm);
    void Regenerate();

    // FITTINGS specific MEP (Batch 2)
    string CreateTeeY(string mainPipeId, Point3D branchPointMm, double branchDnMm,
                      string systemType, string levelName, double angleRad, string pipeTypeName = "");
    string CreateElbow(string pipeIdA, string pipeIdB, Point3D atMm);
    string CreateTransition(string pipeIdA, string pipeIdB, Point3D atMm);

    // CONNECT specific (Batch 2)
    void ConnectPipeToTeeBranch(string pipeId, string teeId);
    void ConnectClosest(string elementIdA, string elementIdB, Point3D nearMm);

    // TRANSACTION
    void RunInTransaction(string name, Action body);
}
```

Đơn vị: tất cả length input/output là **mm** (UnitHelper convert sang feet nội bộ Revit).

### Quy ước pick connector

- **Closest-to-point**: dùng cho Elbow / Transition / ConnectClosest
- **First-unconnected**: CHỈ dùng cho Tee branch (`ConnectPipeToTeeBranch`)
- **KHÔNG dùng "first-unconnected" cho pipe sau BreakPipe** — intermittent fail

## Quy tắc cứng

1. **Feature command KHÔNG gọi Revit API direct**. Dùng `ctx.RevitSvc.*()`.

2. **Mọi modify Revit phải trong transaction** — luôn qua `RevitSvc.RunInTransaction("name", () => { ... })`.

3. **ElementId luôn là string** trong DTO + interface.

4. **Khi thêm method mới** vào IRevitService:
   - Implement ở `RevitService.cs` (real)
   - Implement ở `FakeRevitService.cs` với log `OperationLog` để test verify
   - Add unit test trong `tests/`

## FakeRevitService — test pattern

```csharp
[Fact]
public void DuctRouting_creates_correct_count()
{
    var fake = new FakeRevitService();
    fake.SeedLevels.Add(new LevelData { Id = "level-1", Name = "L1", ElevationMm = 0 });

    var ctx = new TestFeatureContext(fake, /* fake server proxy */);
    var cmd = new DuctRoutingCommand();
    cmd.RunFeatureForTest(ctx);

    Assert.Equal(3, fake.OperationLog.Count(op => op.StartsWith("CreateDuct")));
}
```

`FakeRevitService.OperationLog` là `List<string>` ghi mỗi action. `RunInTransaction` ở Fake log "BeginTransaction" + "CommitTransaction" / "RollbackTransaction".

## Anti-pattern ❌

❌ **Gọi Revit API direct**:
```csharp
Document doc = ctx.UiApp.ActiveUIDocument.Document;
var pipes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeCurves).ToElements();
```

Phải dùng:
```csharp
var pipes = ctx.RevitSvc.GetByCategory(BuiltInCategory.OST_PipeCurves);
```

❌ **Manual transaction**:
```csharp
using (var tx = new Transaction(doc, "Place")) { tx.Start(); ...; tx.Commit(); }
```

Phải dùng:
```csharp
ctx.RevitSvc.RunInTransaction("Place", () => { ... });
```

## Reference

- `src/client/MEPAuto.Client.Common/Revit/IRevitService.cs` — interface
- `src/client/MEPAuto.Client.Common/Revit/RevitService.cs` — real impl
- `src/client/MEPAuto.Client.Common/Revit/FakeRevitService.cs` — fake cho test
- `src/client/MEPAuto.Client.Common/Revit/UnitHelper.cs` — mm ↔ feet
- `src/client/MEPAuto.Client.Common/Revit/ElementIdAdapter.cs` — int/long compat
