# Ribbon Conventions — Panel Group + Order + Naming

Convention cho `IFeatureManifest.PanelGroup` + `Order` + naming. Mục đích: ribbon MEPAuto consistent.

## Tab structure

Tab DUY NHẤT: **MEPAuto** (set ở `RibbonBuilder.TabName`). Không tạo thêm tab mới.

## PanelGroup naming

Format: `MEPAuto - {Category}`. 1 panel = 1 nhóm chức năng liên quan.

Categories chuẩn:

| PanelGroup | Domain | Ví dụ feature |
|---|---|---|
| `MEPAuto - Wet` | Hệ ướt MEP (sprinkler, plumbing, drainage) | Sprinkler placement, pipe routing nước |
| `MEPAuto - Dry` | Hệ khô MEP (HVAC duct, exhaust) | Duct routing, diffuser placement |
| `MEPAuto - Electrical` | Điện | Circuit layout, panel schedule |
| `MEPAuto - HVAC` | Điều hòa không khí | FCU layout, chiller routing |
| `MEPAuto - Modeling` | Thao tác model chung | RenameElements, MoveBatch |
| `MEPAuto - Documentation` | Sheet, view, schedule | TitleBlockUpdate, ViewTemplate apply |
| `MEPAuto - Utilities` | Công cụ phụ trợ | ParameterAudit, CleanupOrphans |
| `MEPAuto - Demo` | Pilot / sandbox | Hello World, test feature |

**Quy tắc**:
- Tên category là **danh từ chung**, không phải tên feature cụ thể
- Tối đa 8 panel — Revit ribbon khó scroll khi nhiều panel

## Order range

| Range | Loại action |
|---|---|
| **10-19** | Cardinal action chính của panel |
| **20-29** | Modify / batch update |
| **30-39** | Query / inspect / report |
| **40-49** | Setup / config / preferences |
| **50-99** | Utility / one-off |

## DisplayName

- **Ngắn**: ≤ 18 ký tự
- **Imperative verb**: "Place Sprinklers", "Route Duct"
- **Tiếng Anh** (default, international team)

## Icon resource path

Format: `Icons/{feature-lower}.png`. File 32×32 PNG, transparent background.

```xml
<ItemGroup>
  <EmbeddedResource Include="Icons\ductrouting.png" />
</ItemGroup>
```

## License feature key

Format: `{name-lowercase}.basic` — match property `LicenseFeature` trong manifest.

```json
{
  "user@company.com": ["ductrouting.basic", "sprinkler.basic"]
}
```

## Manifest example đầy đủ

```csharp
public class DuctRoutingManifest : IFeatureManifest
{
    public string Name => "DuctRouting";
    public string DisplayName => "Route Ducts";
    public string ServerEndpoint => "/api/v1/ductrouting/execute";
    public string LicenseFeature => "ductrouting.basic";
    public string PanelGroup => "MEPAuto - Dry";
    public int Order => 10;
    public string IconResourcePath => "Icons/ductrouting.png";
    public Type CommandType => typeof(DuctRoutingCommand);
}
```

## Reference

- `src/client/MEPAuto.Client.Shell/Ribbon/RibbonBuilder.cs`
- `shared/MEPAuto.Contracts/Manifests/IFeatureManifest.cs`
- `src/client/features/MEPAuto.HelloWorld/Manifest/HelloWorldManifest.cs`
