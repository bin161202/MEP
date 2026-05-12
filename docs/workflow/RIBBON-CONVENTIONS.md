# Ribbon Conventions — Panel Group + Order + Naming

Convention cho `IFeatureManifest.PanelGroup` + `Order` + naming. Mục đích: ribbon MEPAuto consistent, button quen thuộc — user không phải tìm.

## Tab structure

Tab DUY NHẤT: **MEPAuto** (set ở `RibbonBuilder.TabName`). Không tạo thêm tab mới — sẽ rối ribbon Revit.

## PanelGroup naming

Format: `MEPAuto - {Category}`. 1 panel = 1 nhóm chức năng liên quan.

Categories chuẩn:

| PanelGroup | Domain | Ví dụ feature |
|---|---|---|
| `MEPAuto - Wet` | Hệ ướt MEP (sprinkler, plumbing, drainage) | Sprinkler placement, pipe routing nước |
| `MEPAuto - Dry` | Hệ khô MEP (HVAC duct, exhaust) | Duct routing, diffuser placement |
| `MEPAuto - Electrical` | Điện | Circuit layout, panel schedule |
| `MEPAuto - HVAC` | Điều hòa không khí | FCU layout, chiller routing |
| `MEPAuto - Modeling` | Thao tác model chung | RenameElements, MoveBatch, CopyAlongPath |
| `MEPAuto - Documentation` | Sheet, view, schedule | TitleBlockUpdate, ViewTemplate apply |
| `MEPAuto - Utilities` | Công cụ phụ trợ | ParameterAudit, FamilyMigrate, CleanupOrphans |
| `MEPAuto - Demo` | Pilot / sandbox | Hello World (đang có), test feature |
| `MEPAuto - General` | Default fallback nếu chưa rõ category | (tránh dùng — ép mình categorize) |

**Quy tắc**:
- Tên category là **danh từ chung**, không phải tên feature cụ thể
- Tối đa 8 panel — Revit ribbon khó scroll khi nhiều panel
- Khi Phase 2 có 30+ feature → cân nhắc split tab `MEPAuto MEP` + `MEPAuto Modeling` riêng

## Order range

Trong cùng panel, `Order` xác định thứ tự button (asc).

| Range | Loại action | Ví dụ |
|---|---|---|
| **10-19** | Cardinal action chính của panel | "Place Sprinklers", "Route Duct" |
| **20-29** | Modify / batch update | "Rename Elements", "Move Batch" |
| **30-39** | Query / inspect / report | "Audit Parameters", "Find Orphans" |
| **40-49** | Setup / config / preferences | "Set Default Family", "Configure" |
| **50-99** | Utility / one-off | (không nên có nhiều ở range này) |

**Khuyến nghị**: bắt đầu feature mới ở Order chẵn (10, 20, 30) → có space chèn feature liên quan (vd 11, 12) sau này.

## DisplayName

Tên hiển thị trên button — phải:
- **Ngắn**: ≤ 18 ký tự (ribbon button hẹp). Nếu dài hơn → multiline qua `\n`
- **Imperative verb**: "Place Sprinklers", "Rename Marks" (KHÔNG "Sprinkler Tool", "Rename Utility")
- **Tiếng Anh hoặc Tiếng Việt nhất quán** trong cùng PanelGroup. Default: tiếng Anh (international team).

Ví dụ:
```csharp
public string DisplayName => "Place Sprinklers";    // ✅
public string DisplayName => "Sprinkler";            // ❌ — quá generic
public string DisplayName => "Place Sprinkler Tool with Auto-Spacing";  // ❌ — quá dài
```

## Icon resource path

Format: `Icons/{feature-lower}.png`.

- File icon: 32×32 PNG, transparent background. Tools: Figma → export PNG @1x.
- Embed resource trong feature project csproj:
   ```xml
   <ItemGroup>
     <EmbeddedResource Include="Icons\ductrouting.png" />
   </ItemGroup>
   ```
- Convention: 1 icon per feature, KHÔNG share giữa feature.

Thiếu icon? RibbonBuilder fallback default icon (Revit gear) — không break, nhưng ribbon kém pro.

## License feature key

Format: `{name-lowercase}.basic` — match property `LicenseFeature` trong manifest.

Ví dụ: `DuctRoutingCommand` → `LicenseFeature = "ductrouting.basic"`. Server config `/var/mepauto-data/licenses.json`:
```json
{
  "user@company.com": ["ductrouting.basic", "rename.basic"]
}
```

Phase 2 có thể có tier: `ductrouting.basic` vs `ductrouting.pro` (advanced features).

## Manifest example đầy đủ

```csharp
public class DuctRoutingManifest : IFeatureManifest
{
    public string Name => "DuctRouting";                          // Technical key, no space
    public string DisplayName => "Route Ducts";                   // ≤ 18 chars, imperative
    public string ServerEndpoint => "/api/v1/ductrouting/execute";
    public string LicenseFeature => "ductrouting.basic";          // {name-lower}.basic
    public string PanelGroup => "MEPAuto - Dry";                  // category chuẩn
    public int Order => 10;                                       // cardinal action range
    public string IconResourcePath => "Icons/ductrouting.png";    // 32×32 PNG
    public Type CommandType => typeof(DuctRoutingCommand);
}
```

## Khi có conflict Order

2 feature cùng panel cùng Order → ribbon thứ tự không xác định (last loaded wins).

Fix: review codebase trước khi assign Order:
```bash
grep -r "Order =>" src/client/features/ | sort -k2
```

Hoặc dùng convention: feature mới của LEAD cấp range 50-99, member cấp 10-49.

## Reference

- `src/client/MEPAuto.Client.Shell/Ribbon/RibbonBuilder.cs` — scan IFeatureManifest qua reflection
- `shared/MEPAuto.Contracts/Manifests/IFeatureManifest.cs` — interface đầy đủ
- `src/client/features/MEPAuto.HelloWorld/Manifest/HelloWorldManifest.cs` — pilot example