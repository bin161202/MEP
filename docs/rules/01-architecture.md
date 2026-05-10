# Rule 01 — Kiến trúc tổng thể

> **TL;DR**: Mọi feature có **Client thin** (probe + apply) + **Server full** (Domain + Application + Endpoint). Client chỉ gọi `IRevitService` và `IServerProxy`, KHÔNG gọi Revit API direct, KHÔNG có Domain logic. Server chứa thuật toán + license + audit, expose qua HTTP. Contracts là cầu nối thuần data.

## Nguyên tắc

```
┌────────────────────────┐         HTTPS         ┌──────────────────┐
│ Client (Revit)         │ ───────POST─────────► │ Server (VPS)     │
│                        │                       │                  │
│ Manifest               │                       │ Domain (IP)      │
│ Command  ─┐            │                       │ Application      │
│           ├─wrap──►    │                       │ Endpoint         │
│ Contract ─┘ ExecuteHl  │ ◄─────PlanDto──────── │                  │
│ IRevitService          │                       │                  │
│ IServerProxy           │                       │                  │
└────────────────────────┘                       └──────────────────┘
        ▲                                                │
        │ ServerStepDispatcher (AI/CAD-PDF mode)         │
        │ Server đẩy step ──► ExternalEvent ──► Contract │
        │                                                │
        │  Both reference                                │
        │  ┌──────────────────────┐                      │
        └─►│ Contracts (DTOs)     │◄─────────────────────┘
           │ (.NET Standard 2.0)  │
           └──────────────────────┘
```

## Quy tắc cứng

1. **MỌI feature đều có 2 project**: `MEPAuto.{Feature}` (Client) + `MEPAuto.Server.{Feature}` (Server). Không có "Client-only feature" hay "Server-only feature".

2. **Client KHÔNG có Domain logic**. Tất cả thuật toán "trí tuệ" (spacing algorithm, validation, business rules) ở `Server.{Feature}.Domain.*Logic.cs`. Client chỉ:
   - Probe Revit qua `IRevitService.Get*()` → snapshot DTO
   - Gọi `_server.Post<TResp>("/api/v1/.../execute", request)` → nhận `PlanDto`
   - Apply plan qua `IRevitService.RunInTransaction(...)` + `IRevitService.Create*()` / `SetParameter()` / `Move*()`

3. **MỖI feature có 1 `IFeatureContract`** (entry HEADLESS) ⭐
   - Wrap `Command.ExecuteHeadless(ctx, input)` — KHÔNG duplicate logic.
   - 3 mode dùng chung 1 `Execute`: User mode (ribbon click), AI mode (Server đẩy step), CAD-PDF mode (luồng nền).
   - `Execute` KHÔNG được show TaskDialog / WPF dialog / `PickObject` (luồng nền không có UI). UI nằm ở `Command.Execute` trước khi build input.
   - Reflection scan qua `ContractRegistry` ở `Client.Shell` — feature DLL có mặt là tự nhặt, KHÔNG đăng ký thủ công.
   - Workflow chi tiết: `docs/workflow/WORKFLOW-NEW-FEATURE.md` step 6.2.

4. **Server endpoint PHẢI có**: `[Authorize]` + license check inline + audit log. Pattern xem `src/server/features/MEPAuto.Server.HelloWorld/Endpoint/HelloWorldController.cs`.

5. **Contracts là thuần data** (`.NET Standard 2.0`):
   - DTO classes chỉ có property + default constructor
   - KHÔNG reference Revit API, KHÔNG reference Server.Core/Server.Infrastructure
   - 2 nhóm: wire format (`*Request`/`*Response`) + typed Data class (`*Data` — LevelData, ElementSnapshotData, PipeData, FittingData, ...)
   - **Lưu ý 2 nghĩa của "Contracts"**: `shared/MEPAuto.Contracts/` = wire DTO. `src/client/features/.../Contracts/` = `IFeatureContract` impl. KHÔNG nhầm.

6. **Feature KHÔNG được reference feature khác**. Nếu cần data từ feature khác:
   - Client-side: qua `IDataStorageService`
   - Server-side: qua `IDataStorageService` server-side (Phase 1 JSON file, Phase 2 Redis)

## Cấu trúc 1 feature (chuẩn)

```
src/
├── client/features/MEPAuto.{Feature}/
│   ├── MEPAuto.{Feature}.csproj           ProjectReference: Contracts + Client.Common
│   ├── Manifest/{Feature}Manifest.cs      IFeatureManifest impl (8 property)
│   ├── Commands/{Feature}Command.cs       [Transaction] + Execute (User mode) + ExecuteHeadless
│   └── Contracts/{Feature}Contract.cs     IFeatureContract impl — wrap ExecuteHeadless
│
└── server/features/MEPAuto.Server.{Feature}/
    ├── MEPAuto.Server.{Feature}.csproj    FrameworkRef: AspNetCore + ProjectRef: Server.Core
    ├── Domain/{Feature}Logic.cs           Pure algorithm (no DI, no IO)
    ├── Application/{Feature}Service.cs    Orchestration (DI: IAuditLogger)
    └── Endpoint/{Feature}Controller.cs    [Authorize] + license check + Service call

shared/MEPAuto.Contracts/DTOs/
└── {Feature}Dtos.cs                      SnapshotData, Request, Response, ResultRequest
```

## Ví dụ ✅ — DuctRouting (IP nặng, logic dài)

**Client**: probe levels + duct types → POST → server tính routes → apply
```csharp
var snapshot = new DuctRoutingSnapshotDto {
    Levels = ctx.RevitSvc.GetLevels(),
    SelectedElementIds = ctx.RevitSvc.GetSelected().Select(e => e.Id).ToArray(),
};
var plan = ctx.Server.Post<DuctRoutingResponse>("/api/v1/ductrouting/execute",
    new DuctRoutingRequest { Snapshot = snapshot }).GetAwaiter().GetResult();

ctx.RevitSvc.RunInTransaction("Route ducts", () => {
    foreach (var seg in plan.Segments)
        ctx.RevitSvc.CreateDuct(seg.Start, seg.End, seg.WidthMm, seg.HeightMm, seg.SystemType, seg.LevelName);
});
```

## Anti-pattern ❌

❌ **Client gọi Revit API direct**: `commandData.Application.ActiveUIDocument.Document.GetElements()` — phải qua `IRevitService.GetByCategory(...)`.

❌ **Domain logic ở Client**: `if (length > 5000) split ...` ở Command — phải move sang Server `Domain.*Logic`.

❌ **Server endpoint thiếu license check**: dù logic đơn giản, vẫn check `_license.CanUse(User, "feature.basic")`.

❌ **Feature reference feature khác**: `MEPAuto.DuctRouting` reference `MEPAuto.Sprinkler` — đi qua data store.

## Reference

- `src/client/features/MEPAuto.HelloWorld/` — pilot template (đầy đủ Manifest + Commands + Contracts)
- `src/server/features/MEPAuto.Server.HelloWorld/` — pilot server-side template
- `shared/MEPAuto.Contracts/Manifests/IFeatureManifest.cs` — manifest interface
- `src/client/MEPAuto.Client.Common/Contracts/IFeatureContract.cs` — headless entry interface
- `src/client/MEPAuto.Client.Shell/Contracts/ContractRegistry.cs` — reflection scan registry
