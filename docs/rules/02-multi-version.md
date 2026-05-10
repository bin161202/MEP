# Rule 02 — Multi-version Revit 2022-2027

> **TL;DR**: 1 codebase, 12 build config (Debug/Release × 6 version), 2 nhóm runtime (`net48` cho 2022-2024 + `net8.0-windows` cho 2025-2027). Mọi gọi `ElementId` đi qua `ElementIdAdapter`. RevitAPI reference resolve từ Program Files (LEAD machine) hoặc stub (CI runner).

## Build configurations

12 config trong `Directory.Build.props`:

| Config | Framework | DefineConstants | RevitVersion |
|---|---|---|---|
| `Debug-2022` / `Release-2022` | net48 | `REVIT_NET48;REVIT_INT_ID;REVIT_2022` | 2022 |
| `Debug-2023` / `Release-2023` | net48 | `REVIT_NET48;REVIT_INT_ID;REVIT_2023` | 2023 |
| `Debug-2024` / `Release-2024` | net48 | `REVIT_NET48;REVIT_INT_ID;REVIT_2024` | 2024 |
| `Debug-2025` / `Release-2025` | **net8.0-windows** | `REVIT_NET8;REVIT_LONG_ID;REVIT_2025` | 2025 |
| `Debug-2026` / `Release-2026` | net8.0-windows | `REVIT_NET8;REVIT_LONG_ID;REVIT_2026` | 2026 |
| `Debug-2027` / `Release-2027` | net8.0-windows | `REVIT_NET8;REVIT_LONG_ID;REVIT_2027` | 2027 |

Server projects (`*.Server.*` + `Contracts`) target `net8.0` cố định.

Build:
```bash
dotnet build MEPAuto.sln -c Release-2024     # net48
dotnet build MEPAuto.sln -c Release-2025     # net8.0-windows
```

## ElementId compat shim

Revit 2022-2024 dùng `ElementId.IntegerValue` (int), Revit 2025+ dùng `ElementId.Value` (long). **Revit 2024 đã deprecate `IntegerValue`** (CS0618 warning) nhưng vẫn còn để tương thích.

**Quy tắc cứng**: TUYỆT ĐỐI không gọi `.IntegerValue` hoặc `.Value` trên `ElementId` trực tiếp. Đi qua adapter:

```csharp
// ✅ ĐÚNG
long id = ElementIdAdapter.GetValue(element.Id);
ElementId revitId = ElementIdAdapter.Create(123L);

// ❌ SAI — fail CI lint, build warning trên 2024+
long id = element.Id.IntegerValue;          // deprecated trên 2024
long id = element.Id.Value;                  // không có trên 2022-2024
```

CI lint enforce qua `tools/verify-elementid-usage.ps1`. File DUY NHẤT được phép dùng `.IntegerValue` / `.Value` là `src/client/MEPAuto.Client.Common/Revit/ElementIdAdapter.cs` (suppress CS0618 local).

## Khi sửa code Revit-specific

```csharp
// ✅ Dùng feature flag bao trùm hành vi
#if REVIT_LONG_ID
    return id.Value;
#else
    return id.IntegerValue;
#endif

// ❌ Tránh version-specific trừ khi thật sự cần
#if REVIT_2024
    // code đặc thù 2024
#endif
```

→ Khi Revit 2028 ra: thêm config + map flag, KHÔNG sửa code feature.

## RevitAPI reference

`Directory.Build.props` resolve `RevitAPI.dll` theo thứ tự ưu tiên:

1. `C:\Program Files\Autodesk\Revit {RevitVersion}\RevitAPI.dll` — máy LEAD/dev có Revit cài thật
2. Fallback `tools/revit-stubs/{RevitVersion}/RevitAPI.dll` — CI runner không có Revit

Tạo stub 1 lần trên máy LEAD: copy DLL từ `C:\Program Files\Autodesk\Revit {ver}\` vào `tools/revit-stubs/{ver}/`. Commit vào repo private.

## CI matrix

`.github/workflows/ci.yml` build matrix 6 version. Server build job chạy độc lập trên Linux.

```yaml
strategy:
  matrix:
    revit: [2022, 2023, 2024, 2025, 2026, 2027]
```

## Khi Autodesk release Revit mới (vd 2028)

1. Cài Revit 2028, kiểm tra `RevitAPI.dll` có ở `C:\Program Files\Autodesk\Revit 2028\`
2. Đọc release note → check breaking API → có thì update `IRevitService` / `ElementIdAdapter`
3. Thêm `Debug-2028;Release-2028` vào `<Configurations>` của `Directory.Build.props`
4. Map vào nhóm runtime (.NET 8 hoặc mới hơn) + flag
5. CI matrix thêm `2028`
6. Smoke test 1-2 feature pilot trên Revit 2028
7. Cập nhật `MEPAuto.sln` thêm config × project

→ ~1-2 ngày, không refactor business code.

## Reference

- `Directory.Build.props` — full config XML
- `src/client/MEPAuto.Client.Common/Revit/ElementIdAdapter.cs` — shim
- `tools/verify-elementid-usage.ps1` — CI lint
- `tools/revit-stubs/README.md` — stub gen + trạng thái
