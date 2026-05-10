# Rule 08 — ExternalEvent + Async callbacks

> **TL;DR**: Mọi tương tác từ background thread (Timer, Task, async continuation) tới Revit API hoặc UI Revit PHẢI đi qua `ExternalEvent` + `IExternalEventHandler`. KHÔNG gọi trực tiếp — sẽ crash "Unable to execute Revit API outside of Revit API context" hoặc UI hang.

## Tại sao có rule này

Revit API chỉ chạy được trên **Revit's main thread** (UI thread). 4 trường hợp MEPAuto chắc chắn gặp:

1. **HeartbeatService Timer** (30s tick) — fail 3 lần → cần show TaskDialog "Mất kết nối MEPAuto" + grey-out ribbon. Timer chạy background thread → KHÔNG show TaskDialog trực tiếp.
2. **Async ServerProxy continuation** — long-running POST trả về trên thread pool.
3. **License revoke push** (Phase 2) — server push event qua SignalR/long-poll → Client cần force-logout.
4. **Auto-update** — background download → khi xong cần TaskDialog "Phiên bản mới, restart Revit".

## Pattern chuẩn

### 1. Định nghĩa Handler

```csharp
// MEPAuto.Client.Common/Events/OfflineNoticeHandler.cs
public class OfflineNoticeHandler : IExternalEventHandler
{
    public string Message { get; set; } = "";

    public void Execute(UIApplication app)
    {
        TaskDialog.Show("MEPAuto", Message);
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
        var offlineEvent = ExternalEvent.Create(offlineHandler);  // PHẢI trong OnStartup
        OfflineNotifier.Bind(offlineHandler, offlineEvent);
        return Result.Succeeded;
    }
}
```

`ExternalEvent.Create` PHẢI gọi từ Revit API context (OnStartup, ICommand, ...).

### 3. Raise từ background

```csharp
// HeartbeatService — chạy Timer background
private void OnHeartbeatFailed()
{
    if (_consecutiveFailures < FailThreshold) return;
    SetOnline(false);
    OfflineNotifier.Raise("Mất kết nối server MEPAuto. Toàn bộ feature đã grey-out.");
}
```

## Static facade pattern ⭐

`Common` không reference `Shell`. Pattern: **static facade** trong `Common/Events/` — Shell wire `Bind(handler, evt)` 1 lần, luồng nền gọi `Raise(...)` / `Dispatch(...)` mà không cần biết ExternalEvent instance.

```csharp
public static class OfflineNotifier
{
    private static OfflineNoticeHandler? _handler;
    private static ExternalEvent? _event;

    public static void Bind(OfflineNoticeHandler h, ExternalEvent e) { _handler=h; _event=e; }

    public static void Raise(string message)
    {
        if (_handler == null || _event == null) return;
        _handler.Message = message;
        _event.Raise();
    }
}
```

## ServerStep dispatcher — AI / CAD-PDF mode ⭐

Server đẩy step xuống Client. Luồng:

```
Server → JobPoller poll GET /api/v1/jobs/{id}/next-step
       → nhận StepRequest{JobId, FeatureName, InputJson}
       → ServerStepDispatcher.Dispatch(req)
         → ServerStepHandler.Enqueue(req)
         → ExternalEvent.Raise()
       ↓ Revit idle frame
       → ServerStepHandler.Execute(app)
         → ContractRegistry.Resolve(FeatureName)
         → contract.Execute(ctx, input)
         → req.OnComplete?.Invoke(output, null)
       → HTTP POST /jobs/{id}/result
```

## Khi nào CẦN ExternalEvent

| Code chạy ở | Tương tác | ExternalEvent? |
|---|---|---|
| `IExternalCommand.Execute` (ribbon click) | Revit API + TaskDialog | KHÔNG — đã ở context an toàn |
| `Timer.Callback` (HeartbeatService) | TaskDialog | **CẦN** |
| `Timer.Callback` | Ribbon enable/disable | **CẦN** |
| `Task.Run` background | Bất cứ Revit API | **CẦN** |
| `await server.Post(...).ConfigureAwait(false)` continuation | Revit API | **CẦN** |

## Pitfall thực tế

### 1. Tạo ExternalEvent ngoài Revit context

```csharp
// HeartbeatService constructor ❌
public HeartbeatService(IServerProxy s) {
    _event = ExternalEvent.Create(handler);  // crash
}
```

Fix: tạo trong `RevitApp.OnStartup`, expose qua static facade.

### 2. Raise nhiều lần liên tục

Fix: dedupe — chỉ raise khi `IsOnline` chuyển true → false.

### 3. Heavy work trong Execute

Handler `Execute` phải < 100ms. Do work trên background, raise event chỉ để show kết quả.

## Anti-pattern ❌

❌ **Gọi TaskDialog từ Timer**:
```csharp
new Timer(_ => TaskDialog.Show("...", "..."), null, 0, 30_000);  // ❌ crash
```

❌ **Tạo ExternalEvent mỗi lần raise**:
```csharp
public void NotifyOffline() {
    var e = ExternalEvent.Create(new OfflineHandler());  // ❌ leak
    e.Raise();
}
```

❌ **Contract.Execute show TaskDialog / WPF / PickObject** — luồng nền không có UI.

## Reference

- `src/client/MEPAuto.Client.Shell/RevitApp.cs` — đăng ký ExternalEvent + Bind facade
- `src/client/MEPAuto.Client.Common/Events/OfflineNoticeHandler.cs`
- `src/client/MEPAuto.Client.Common/Events/OfflineNotifier.cs` — static facade
- `src/client/MEPAuto.Client.Common/Events/ServerStepHandler.cs`
- `src/client/MEPAuto.Client.Common/Events/ServerStepDispatcher.cs`
- `src/client/MEPAuto.Client.Common/Auth/HeartbeatService.cs`
