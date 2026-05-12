# Rule 08 — ExternalEvent + Async callbacks

> **TL;DR**: Mọi tương tác từ background thread (Timer, Task, async continuation) tới Revit API hoặc UI Revit (TaskDialog, ribbon update) PHẢI đi qua `ExternalEvent` + `IExternalEventHandler`. KHÔNG gọi trực tiếp — sẽ crash "Unable to execute Revit API outside of Revit API context" hoặc UI hang.

## Tại sao có rule này

Revit API chỉ chạy được trên **Revit's main thread** (UI thread). 4 trường hợp Client MEPAuto chắc chắn gặp:

1. **HeartbeatService Timer** (30s tick) — fail 3 lần → cần show TaskDialog "Mất kết nối" + grey-out ribbon. Timer chạy background thread → KHÔNG show TaskDialog trực tiếp.
2. **Async ServerProxy continuation** — long-running POST trả về sau khi user click feature, nếu code dùng `await` (thay `.GetAwaiter().GetResult()`) → continuation có thể chạy thread pool thread, KHÔNG được gọi Revit API.
3. **License revoke push** (Phase 2) — server push event qua SignalR/long-poll → Client cần force-logout user đang giữa session.
4. **Auto-update** — background download → khi xong cần TaskDialog "Phiên bản mới, restart Revit".

Rule cứng: feature command sync (đã chạy trong Revit context) KHÔNG cần ExternalEvent. Background work KHÔNG được tạm thời "hijack" Revit context — phải đăng ký event và Revit sẽ gọi handler ở context an toàn.

## Pattern chuẩn

### 1. Định nghĩa Handler

```csharp
// MEPAuto.Client.Common/Events/OfflineNoticeHandler.cs
public class OfflineNoticeHandler : IExternalEventHandler
{
    public string Message { get; set; } = "";  // mutable state — set trước khi Raise()

    public void Execute(UIApplication app)
    {
        // Đang ở Revit API context an toàn — gọi Revit API + TaskDialog OK
        TaskDialog.Show("MEPAuto", Message);
        // TODO Phase 2: grey-out ribbon panels qua RibbonHelper.SetEnabled(false)
    }

    public string GetName() => "MEPAuto.OfflineNotice";
}
```

### 2. Init 1 lần lúc startup (RevitApp.OnStartup)

```csharp
public class RevitApp : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        var offlineHandler = new OfflineNoticeHandler();
        var offlineEvent = ExternalEvent.Create(offlineHandler);  // PHẢI tạo trong OnStartup
        OfflineNotifier.Bind(offlineHandler, offlineEvent);
        // ... ribbon scan, auth bootstrap ...
        return Result.Succeeded;
    }
}
```

`ExternalEvent.Create` PHẢI gọi từ Revit API context (OnStartup, ICommand, ...). KHÔNG tạo từ background thread.

### 3. Raise từ background

```csharp
// HeartbeatService — chạy Timer background
private void OnHeartbeatFailed()
{
    if (_consecutiveFailures < FailThreshold) return;
    SetOnline(false);

    // Background thread — KHÔNG gọi TaskDialog trực tiếp
    OfflineNotifier.Raise("Mất kết nối server MEPAuto. Toàn bộ feature đã grey-out.");
}
```

`Raise()` non-blocking — return ngay, Revit dispatch event ở idle frame tiếp theo.

## Khi nào CẦN ExternalEvent (decision matrix)

| Code chạy ở | Tương tác | ExternalEvent? |
|---|---|---|
| `IExternalCommand.Execute` (ribbon click) | Revit API + TaskDialog | KHÔNG — đã ở context an toàn |
| `Timer.Callback` (HeartbeatService) | TaskDialog | **CẦN** |
| `Timer.Callback` | Ribbon enable/disable | **CẦN** |
| `Task.Run` background | Bất cứ Revit API | **CẦN** |
| `await server.Post(...).ConfigureAwait(false)` continuation | Revit API | **CẦN** (continuation ở thread pool) |
| `IEventHandler<DocumentOpened>` | Revit API | KHÔNG — Revit gọi event này ở UI thread |
| `WPF Window.OnClosed` | Revit API | KHÔNG — WPF event ở UI thread (= Revit thread) |

## ExternalEvent flow (chi tiết)

```
Background thread                          Revit UI thread
─────────────────                          ────────────────
Timer tick 30s                             (Revit idle loop)
  ↓
heartbeat fail counter++
  ↓
fail >= 3
  ↓
handler.Message = "..."
  ↓
event.Raise()  ────────────────────►  queue event
  ↓ return ngay                            ↓
Timer return                               (next idle frame, ~16ms)
                                           ↓
                                    handler.Execute(app)
                                      → TaskDialog.Show
                                      → ribbon update
                                      → user thấy notice
```

Latency: 1 idle frame (~16-100ms tùy Revit busy). User experience instant.

## Pitfall thực tế

### 1. Tạo ExternalEvent ngoài Revit context

```csharp
// HeartbeatService constructor (có thể được DI new ngoài OnStartup) ❌
public HeartbeatService(IServerProxy s) {
    _event = ExternalEvent.Create(handler);  // crash "Unable to execute Revit API outside of Revit API context"
}
```

Fix: tạo trong `RevitApp.OnStartup`, expose qua static facade — service nào cần dùng tham chiếu static.

### 2. Handler không thread-safe

```csharp
public class MyHandler : IExternalEventHandler {
    public string Message { get; set; }  // ❌ background thread set + UI thread đọc → race
}
```

Fix: dùng `lock` hoặc `volatile` cho field handler. Hoặc enqueue message vào `ConcurrentQueue` rồi `Execute` drain.

### 3. Raise nhiều lần liên tục

```csharp
// Mỗi tick fail → raise → 90s = 3 raise → 3 TaskDialog xếp hàng
event.Raise();
```

Fix: dedupe — chỉ raise khi `IsOnline` chuyển true → false (`SetOnline` đã có `changed` check).

### 4. Quên ConfigureAwait(false) trong async chain

Lý do duplicate với rule 05 anti-pattern #3 — nhưng riêng case có ExternalEvent: nếu `await` trong handler `Execute` mà thiếu `ConfigureAwait(false)`, continuation muốn về Revit thread → đã ở Revit thread → ngay continuation đã chiếm chỗ next idle → cascade hang. Best practice: handler `Execute` là **sync only**.

### 5. Heavy work trong Execute

```csharp
public void Execute(UIApplication app) {
    // ❌ block Revit UI 5s
    var data = LongDatabaseQuery();
    TaskDialog.Show("MEPAuto", $"Result: {data}");
}
```

Fix: do work trên background, raise event chỉ để show kết quả. Handler `Execute` <100ms.

## Static facade pattern — tách Common khỏi Shell ⭐

`Common` không reference `Shell` (cycle ref) nhưng `ExternalEvent.Create` PHẢI gọi từ `RevitApp.OnStartup` (Shell). Pattern: **static facade** trong `Common/Events/` — Shell wire `Bind(handler, evt)` 1 lần, luồng nền chỉ gọi `Raise(...)` / `Dispatch(...)` mà không cần biết ExternalEvent instance.

```csharp
// Common/Events/OfflineNotifier.cs (static facade)
public static class OfflineNotifier
{
    private static OfflineNoticeHandler? _handler;
    private static ExternalEvent? _event;

    public static void Bind(OfflineNoticeHandler h, ExternalEvent e) { _handler=h; _event=e; }

    public static void Raise(string message)
    {
        if (_handler == null || _event == null) return;  // chưa Bind → no-op (test, hoặc trước OnStartup xong)
        _handler.Message = message;
        _event.Raise();
    }
}
```

`HeartbeatService` chỉ gọi `OfflineNotifier.Raise(msg)` — KHÔNG biết tới `ExternalEvent`. Test không bind → no-op, không crash.

## ServerStep dispatcher — AI / CAD-PDF mode ⭐

Khác với offline notice (Client → Client UI), AI/CAD-PDF mode là **Server đẩy step xuống Client** (Phase C). Luồng:

```
Server                                    Client
─────                                     ──────
Tính step tiếp theo (LangGraph plan)
  ↓
JobPoller HTTP poll                       (background thread Timer)
  ↓
GET /api/v1/jobs/{id}/next-step
  ↓                                       (nhận StepRequest{JobId,FeatureName,InputJson})
                                          ↓
                                          ServerStepDispatcher.Dispatch(req)
                                            → ServerStepHandler.Enqueue(req)
                                            → ExternalEvent.Raise()
                                          ↓ Revit idle frame
                                          ServerStepHandler.Execute(app)
                                            → drain queue
                                            → ContractRegistry.Resolve(FeatureName)
                                            → JsonConvert.DeserializeObject(InputJson, contract.InputType)
                                            → contract.Execute(ctx, input)
                                            → req.OnComplete?.Invoke(output, null)
                                          ↓
JobPoller POST result ←───────────────  HTTP POST /jobs/{id}/result
```

### Quy tắc cứng

1. **`StepRequest.FeatureName` PHẢI khớp `IFeatureContract.FeatureName`** (= `Manifest.Name`). Mismatch → `Resolve` throw.
2. **Handler `Execute` drain queue, mỗi step try/catch riêng** — 1 step fail KHÔNG block step sau.
3. **Contract.Execute KHÔNG được show TaskDialog / WPF / PickObject** — luồng nền không có UI (rule 09 enforce).
4. **`OnComplete` callback CHỈ chạy sync trong handler** — JobPoller dùng để POST result. KHÔNG block lâu (xem "Heavy work trong Execute").

### Pattern code

```csharp
// Common/Events/ServerStepHandler.cs — drain queue, dispatch sang Contract
public void Execute(UIApplication app) {
    while (_queue.TryDequeue(out var req)) {
        try {
            var ctx = _contextFactory(app);
            var contract = _registry.Resolve(req.FeatureName);
            var input = JsonConvert.DeserializeObject(req.InputJson, contract.InputType);
            var output = contract.Execute(ctx, input);
            req.OnComplete?.Invoke(output, null);
        }
        catch (Exception ex) { req.OnComplete?.Invoke(null, ex); }
    }
}

// Common/Events/ServerStepDispatcher.cs — static facade cho luồng nền
public static class ServerStepDispatcher {
    public static void Bind(ServerStepHandler h, ExternalEvent e) { ... }
    public static bool Dispatch(StepRequest req) {
        if (_handler == null || _event == null) return false;  // chưa Bind
        _handler.Enqueue(req);
        _event.Raise();
        return true;
    }
}

// Shell/RevitApp.cs OnStartup — wire Bind 1 lần
var stepHandler = new ServerStepHandler(_registry, app => new FeatureContext(app, ...));
var stepEvent = ExternalEvent.Create(stepHandler);
ServerStepDispatcher.Bind(stepHandler, stepEvent);

// JobPollerService (Phase C) — background gọi Dispatch
ServerStepDispatcher.Dispatch(new StepRequest {
    JobId = job.Id,
    FeatureName = job.FeatureName,
    InputJson = job.InputJson,
    OnComplete = (out, err) => _ = PostResultAsync(job.Id, out, err)
});
```

## Phase 1 vs Phase 2

**Phase 1** (hiện tại):
- `OfflineNoticeHandler` + `OfflineNotifier` (facade) — heartbeat fail → modal "Mất kết nối"
- `ServerStepHandler` + `ServerStepDispatcher` (facade) — skeleton sẵn cho AI/CAD-PDF mode (JobPoller chưa wire)

`HeartbeatService` dùng pure C# event (`OnlineStateChanged`) flip flag `IsOnline` — `BaseFeatureCommand` đọc flag ở Revit context (ribbon click) → an toàn.

**Phase 2** (sau): các handler dự kiến thêm:
- `LicenseRevokedHandler` — server push → force logout
- `UpdateAvailableHandler` — auto-update background → modal "restart Revit"
- `ServerPushHandler` — SignalR generic event router

Tất cả đăng ký 1 lần ở `RevitApp.OnStartup`.

## Anti-pattern ❌

❌ **Gọi TaskDialog từ Timer**:
```csharp
new Timer(_ => TaskDialog.Show("...", "..."), null, 0, 30_000);  // ❌ crash hoặc invisible
```

❌ **Tạo ExternalEvent mỗi lần raise**:
```csharp
public void NotifyOffline() {
    var e = ExternalEvent.Create(new OfflineHandler());  // ❌ leak — tạo 1 lần ở OnStartup
    e.Raise();
}
```

❌ **Truyền state qua biến closure**:
```csharp
public void NotifyOffline(string msg) {
    Action<UIApplication> body = app => TaskDialog.Show("MEPAuto", msg);
    // không có cơ chế "raise(closure)" trong ExternalEvent API
}
```
ExternalEvent API chỉ có `Raise()` không param — phải set field handler trước.

❌ **Contract.Execute show TaskDialog / WPF / PickObject** — luồng nền không có UI.

## Reference

- `src/client/MEPAuto.Client.Shell/RevitApp.cs` — đăng ký ExternalEvent + Bind facade
- `src/client/MEPAuto.Client.Common/Events/OfflineNoticeHandler.cs` — heartbeat fail handler
- `src/client/MEPAuto.Client.Common/Events/OfflineNotifier.cs` — static facade
- `src/client/MEPAuto.Client.Common/Events/ServerStepHandler.cs` — drain queue + Contract dispatch
- `src/client/MEPAuto.Client.Common/Events/ServerStepDispatcher.cs` — static facade cho luồng nền
- `src/client/MEPAuto.Client.Common/Events/StepRequest.cs` — DTO step + OnComplete callback
- `src/client/MEPAuto.Client.Common/Auth/HeartbeatService.cs` — Timer fire OnlineStateChanged
- `src/client/MEPAuto.Client.Common/Commands/BaseFeatureCommand.cs` — đọc IsOnline trước khi Execute feature
- Autodesk Revit API doc — `ExternalEvent`, `IExternalEventHandler`