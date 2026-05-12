# Rule 09 — Feature project structure + Directory.Build.props

> **TL;DR**: 1 feature = 2 project (`MEPAuto.{Feature}` + `MEPAuto.Server.{Feature}`) + 1 file DTO (`{Feature}Dtos.cs` trong Contracts). KHÔNG NuGet — direct project reference. KHÔNG sửa csproj feature ngoài template — TargetFramework / DefineConstants do `Directory.Build.props` quản. Chạy `tools/add-feature/new-feature.ps1` thay vì tạo file tay.

## Monorepo layout

```
MEPAuto/
├── MEPAuto.sln                            ← solution duy nhất, chứa tất cả project
├── Directory.Build.props                  ← MSBuild common — 12 build config
├── Directory.Packages.props               ← CPM (central package management)
│
├── shared/
│   └── MEPAuto.Contracts/                 ← .NET Standard 2.0 — wire DTO + IFeatureManifest
│       ├── DTOs/                          ← {Feature}Dtos.cs, GeometryDtos, PlacementData, ...
│       ├── Manifests/IFeatureManifest.cs
│       └── Auth/AuthDtos.cs
│
├── src/
│   ├── client/                            ← Revit add-in side
│   │   ├── MEPAuto.Client.Shell/          ← IExternalApplication
│   │   │   ├── RevitApp.cs                ← OnStartup: ribbon scan + ExternalEvent.Create + Bind facade
│   │   │   ├── Ribbon/RibbonBuilder.cs    ← reflection scan IFeatureManifest → build ribbon
│   │   │   └── Contracts/ContractRegistry.cs ← reflection scan IFeatureContract
│   │   ├── MEPAuto.Client.Common/         ← IRevitService, ServerProxy, Auth, BaseFeatureCommand
│   │   │   ├── Revit/                     ← IRevitService + Real/Fake impl + ElementIdAdapter + UnitHelper
│   │   │   ├── Auth/                      ← JwtCache, HeartbeatService, ServerProxy, FeatureContext, LoginDialog
│   │   │   ├── Commands/BaseFeatureCommand.cs
│   │   │   ├── Contracts/                 ← IFeatureContract, IContractRegistry (interface only)
│   │   │   └── Events/                    ← OfflineNoticeHandler + OfflineNotifier facade,
│   │   │                                    ServerStepHandler + ServerStepDispatcher facade + StepRequest
│   │   └── features/
│   │       ├── MEPAuto.HelloWorld/        ← pilot — minimal template
│   │       └── MEPAuto.{Feature}/         ← thêm feature ở đây
│   │
│   └── server/                            ← VPS side (net8.0)
│       ├── MEPAuto.Server.Api/            ← ASP.NET Core entry, Program.cs DI, Controllers/, Middleware/, Auth/
│       ├── MEPAuto.Server.Core/           ← Abstractions (IUserRepository, ILicenseService, ...)
│       ├── MEPAuto.Server.Infrastructure.FileSystem/  ← Phase 1 JSON impls
│       └── features/
│           ├── MEPAuto.Server.HelloWorld/
│           ├── MEPAuto.Server.Versioning/
│           └── MEPAuto.Server.{Feature}/
│
├── tests/
│   ├── MEPAuto.Server.Tests/              ← net8.0 — xUnit Domain + Service
│   └── MEPAuto.Client.IntegrationTests/   ← net48 — IRevitService + PlanApplier với FakeRevitService
│
├── tools/
│   ├── add-feature/                       ← new-feature.ps1 + template
│   ├── dev-setup/                         ← install-revit-addin-manager + test prompts
│   ├── dev-seed/                          ← seed user/license dev
│   ├── deploy/                            ← VPS deploy scripts (docker-compose, nginx*.conf, deploy.sh, ...)
│   ├── revit-stubs/                       ← gen 1 lần cho CI runner không có Revit
│   ├── golden-capture/                    ← regression baseline scenarios
│   ├── obfuscation/                       ← ConfuserEx 2 wrapper (release build)
│   ├── verify-elementid-usage.ps1
│   ├── version-stamp.ps1
│   └── build-revit-stubs.ps1
│
├── installer/                             ← WiX 4 MSI — Product.wxs + addin-manifests/
└── docs/
    ├── rules/                             ← 01..09 rules
    └── workflow/                          ← cẩm nang member
```

> **Lưu ý "Contracts" có 2 nghĩa**:
> - `shared/MEPAuto.Contracts/` = wire format DTO (Client ↔ Server qua HTTP).
> - `src/client/.../Contracts/` = `IFeatureContract` impl (entry HEADLESS — User/AI/CAD-PDF mode).
> Xem rule 01 + rule 07.

## Project naming convention

| Loại | Format | Ví dụ |
|---|---|---|
| Client feature | `MEPAuto.{Feature}` | `MEPAuto.DuctRouting` |
| Server feature | `MEPAuto.Server.{Feature}` | `MEPAuto.Server.DuctRouting` |
| Folder | `src/client/features/MEPAuto.{Feature}/` | `src/client/features/MEPAuto.DuctRouting/` |
| DTO file | `shared/MEPAuto.Contracts/DTOs/{Feature}Dtos.cs` | `DuctRoutingDtos.cs` |
| Namespace Client | `MEPAuto.{Feature}.{SubArea}` | `MEPAuto.DuctRouting.Commands` |
| Namespace Server | `MEPAuto.Server.{Feature}.{SubArea}` | `MEPAuto.Server.DuctRouting.Domain` |
| Assembly | trùng project name | `MEPAuto.DuctRouting.dll` |
| License key | `{feature-lower}.basic` | `ductrouting.basic` |
| Endpoint | `/api/v1/{feature-lower}/...` | `/api/v1/ductrouting/execute` |

`{Feature}`: PascalCase, không dấu space, không kí tự đặc biệt, regex `^[A-Z][a-zA-Z0-9]+$`. Đã enforce ở `new-feature.ps1` `[ValidatePattern]`.

## Cấu trúc file 1 feature (chuẩn)

```
src/client/features/MEPAuto.DuctRouting/
├── MEPAuto.DuctRouting.csproj          ProjectRef: Contracts + Client.Common
├── Manifest/
│   └── DuctRoutingManifest.cs          IFeatureManifest impl (8 property)
├── Commands/
│   └── DuctRoutingCommand.cs           [Transaction] + Execute (User mode) + ExecuteHeadless
├── Contracts/                          ⭐ HEADLESS entry — bắt buộc mỗi feature
│   └── DuctRoutingContract.cs          IFeatureContract impl — wrap Command.ExecuteHeadless
├── Views/                              (optional, khi cần WPF dialog — sinh qua -WithUi)
│   └── DuctRoutingWindow.xaml(.cs)     Window thuần — code-behind chỉ wire OK button
├── ViewModels/                         (optional, đi cặp với Views/)
│   └── DuctRoutingWindowViewModel.cs   INotifyPropertyChanged + Validate(out error)
├── Icons/                              (optional) embedded resource
│   └── ductrouting.png                 32×32 PNG
└── (no Domain logic — di sang Server)

src/server/features/MEPAuto.Server.DuctRouting/
├── MEPAuto.Server.DuctRouting.csproj   FrameworkRef: AspNetCore + ProjectRef: Server.Core
├── Domain/
│   └── DuctRoutingLogic.cs             pure function — algorithm
├── Application/
│   └── DuctRoutingService.cs           orchestration (DI: IAuditLogger, IDataStorageService)
└── Endpoint/
    └── DuctRoutingController.cs        [Authorize] + license check inline + Service call

shared/MEPAuto.Contracts/DTOs/
└── DuctRoutingDtos.cs                  4 class: SnapshotData, Request, Response, ResultRequest
```

### Vai trò 3 entry point cùng 1 feature

| Entry | Khi nào | Ai gọi | UI cho phép? |
|---|---|---|---|
| `Command.Execute` (User mode) | Ribbon click | Revit | ✓ TaskDialog/WPF/PickObject |
| `Command.ExecuteHeadless` | Logic thuần (probe + server + apply) | Cả User mode lẫn Contract gọi | ✗ KHÔNG show UI |
| `Contract.Execute` | AI / CAD-PDF mode | `ServerStepHandler` (xem rule 08) | ✗ KHÔNG show UI |

`Command.Execute` build input (UI/pick) → gọi `ExecuteHeadless`. `Contract.Execute` nhận input từ JSON → gọi `ExecuteHeadless`. → Logic chính chỉ viết 1 lần.

## Csproj template (KHÔNG sửa — generated từ template)

### Client feature csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>MEPAuto.{Feature}</RootNamespace>
    <AssemblyName>MEPAuto.{Feature}</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\shared\MEPAuto.Contracts\MEPAuto.Contracts.csproj" />
    <ProjectReference Include="..\..\MEPAuto.Client.Common\MEPAuto.Client.Common.csproj" />
  </ItemGroup>
</Project>
```

**KHÔNG khai báo** `<TargetFramework>`, `<DefineConstants>`, Revit reference — `Directory.Build.props` đã setup theo nhóm runtime + RevitVersion.

### Server feature csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>MEPAuto.Server.{Feature}</RootNamespace>
    <AssemblyName>MEPAuto.Server.{Feature}</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\..\shared\MEPAuto.Contracts\MEPAuto.Contracts.csproj" />
    <ProjectReference Include="..\..\MEPAuto.Server.Core\MEPAuto.Server.Core.csproj" />
  </ItemGroup>
</Project>
```

## Directory.Build.props — vai trò

File này áp dụng cho **mọi** project trong `MEPAuto.sln`. Set:

1. **12 build config**: `Debug-{2022..2027}`, `Release-{2022..2027}`.
2. **Map config → RevitVersion**: `$(Configuration.EndsWith('-2024'))` → `RevitVersion=2024`.
3. **Phân loại project** qua `MSBuildProjectName.Contains('.Server')` etc → flag `_IsClient`/`_IsServer`/`_IsContracts`/`_IsTests`.
4. **TargetFramework theo nhóm**:
   - Client + RevitVersion 2022-2024 → `net48` + WPF + REVIT_INT_ID
   - Client + RevitVersion 2025-2027 → `net8.0-windows` + WPF + REVIT_LONG_ID
   - Server → `net8.0` cố định
   - Contracts → `netstandard2.0`
   - Tests → `net8.0`
5. **Revit DLL reference** chỉ cho Client, hint Program Files → fallback stub.
6. **Validation early**: Client mà `RevitVersion` rỗng → fail compile sớm với message rõ.

→ Feature mới csproj chỉ cần khai `RootNamespace` + `AssemblyName` + ProjectReference + (optional) PackageReference.

## Quy tắc cứng

1. **KHÔNG sửa Directory.Build.props per project**: nếu cần override, dùng `Directory.Build.targets` riêng — nhưng phải có lý do mạnh + LEAD review. 99% case không cần.

2. **KHÔNG `<TargetFrameworks>` (số nhiều)** trong csproj feature: gây build cross-product config × framework, slow + duplicate output. Single TF qua `Directory.Build.props` là đủ.

3. **KHÔNG `<ProjectReference>` giữa 2 feature**: `MEPAuto.DuctRouting` KHÔNG reference `MEPAuto.Sprinkler`. Đi qua `IDataStorageService` (rule 07) hoặc DTO trong Contracts.

4. **`<PackageReference>` không version trong feature csproj**: version định nghĩa ở `Directory.Packages.props` (CPM enabled). Newtonsoft.Json là exception đã có sẵn.

5. **KHÔNG đổi `OutputPath`**: `Directory.Build.props` set `bin\{Configuration}\` với `AppendTargetFrameworkToOutputPath=false`. Đổi → break copy DLL script + RAM Reload.

6. **Tạo feature mới qua script**: `powershell -ExecutionPolicy Bypass -File tools/add-feature/new-feature.ps1 -Name X`. KHÔNG copy thủ công folder Hello-World rồi rename — dễ sót sln entry, ProjectReference Client.Shell, DI Program.cs.

7. **Feature có WPF dialog → dùng `-WithUi`**: chạy `... -Name X -WithUi` để sinh sẵn `Views/{X}Window.xaml(.cs)` + `ViewModels/{X}WindowViewModel.cs` + Command đã wire ShowDialog. KHÔNG đặt `.xaml` ở chỗ khác (vd thư mục `UI/`, `Dialogs/`) — cấu trúc khác sẽ phá tooling sau này. KHÔNG đặt business logic / Revit API call / ServerProxy call trong code-behind hay VM — code-behind chỉ wire OK button + DialogResult, VM chỉ giữ state + Validate. Mọi network/Revit call ở Command sau khi `ShowDialog() == true`.

## Khi cần thêm 1 dependency mới

### Package từ NuGet (vd Polly cho retry)

1. Edit `Directory.Packages.props` thêm `<PackageVersion Include="Polly" Version="8.0.0" />`.
2. Trong feature csproj (Server hoặc Client) thêm `<PackageReference Include="Polly" />` (KHÔNG `Version`).
3. Build verify pass cả 2024 + 2025.

### Method mới cho IRevitService

Xem rule 04 — add interface method + impl Real + impl Fake + test.

### DTO mới chung nhiều feature

Add vào file class chung trong `shared/MEPAuto.Contracts/DTOs/RevitSnapshotData.cs` hoặc tạo file mới `shared/MEPAuto.Contracts/DTOs/{Domain}Data.cs` (vd `MepFlowData.cs` cho HVAC).

## Sln entry quy tắc

`MEPAuto.sln` có folder structure:
```
src/
├── client/
│   ├── MEPAuto.Client.Shell
│   ├── MEPAuto.Client.Common
│   └── features/
│       ├── MEPAuto.HelloWorld
│       └── MEPAuto.{Feature}
└── server/
    ├── MEPAuto.Server.Api
    ├── MEPAuto.Server.Core
    ├── MEPAuto.Server.Infrastructure.FileSystem
    └── features/
        ├── MEPAuto.Server.HelloWorld
        ├── MEPAuto.Server.Versioning
        └── ...
shared/
└── MEPAuto.Contracts
tests/
└── ...
```

`new-feature.ps1` tự `dotnet sln add` đúng folder. Manual edit `.sln` rất dễ sai GUID — TRÁNH.

## Anti-pattern ❌

❌ **Copy Hello-World folder + rename file**: bỏ qua sln add + Program.cs DI register + ProjectReference Client.Shell. Build pass nhưng ribbon không xuất hiện feature.

❌ **TargetFramework cứng trong csproj feature**:
```xml
<TargetFramework>net48</TargetFramework>  <!-- ❌ override Directory.Build.props -->
```
→ build `Release-2025` cũng output net48 → load fail trên Revit 2025.

❌ **PackageReference với Version trong feature**:
```xml
<PackageReference Include="Polly" Version="7.2.0" />  <!-- ❌ — version drift giữa feature -->
```
CPM enabled → version global ở `Directory.Packages.props`.

❌ **Feature reference Server.Infrastructure direct**:
```xml
<!-- MEPAuto.Server.DuctRouting.csproj -->
<ProjectReference Include="..\..\MEPAuto.Server.Infrastructure.FileSystem\..." />  <!-- ❌ -->
```
Feature chỉ reference `Server.Core` (interface). DI ở `Server.Api/Program.cs` chọn impl.

❌ **Sửa OutputPath**:
```xml
<OutputPath>..\..\..\..\dist\</OutputPath>  <!-- ❌ -->
```
RAM hot-reload + script copy DLL hard-code path `bin\{Config}\` — đổi → break workflow.

❌ **Bỏ `Contracts/{Feature}Contract.cs`**: feature chạy được User mode (ribbon click) nhưng AI/CAD-PDF mode (`ServerStepHandler.Resolve`) throw `"Không tìm thấy IFeatureContract tên 'X'"`. Mỗi feature mới PHẢI có Contract — `new-feature.ps1` sinh sẵn từ template `tools/add-feature/template-feature/client/Contracts/Contract.cs.template`, đừng xóa. Xem rule 01 + 08.

❌ **Contract.Execute show TaskDialog / WPF dialog / PickObject**:
```csharp
public object Execute(IFeatureContext ctx, object input) {
    TaskDialog.Show("...", "...");                 // ❌ luồng nền không show được
    var ref = ctx.UiDoc.Selection.PickObject(...); // ❌ luồng nền không có user pick
}
```
UI chỉ ở `Command.Execute` (User mode) — build input rồi gọi `ExecuteHeadless`. `Contract.Execute` wrap `ExecuteHeadless` luôn, không show gì.

❌ **Code-behind / ViewModel gọi Revit API hoặc ServerProxy**:
```csharp
// ❌ trong DuctRoutingWindow.xaml.cs hoặc DuctRoutingWindowViewModel.cs
private void Ok_Click(object sender, RoutedEventArgs e) {
    var elements = ctx.RevitSvc.GetSelected();   // ❌ View không có quyền truy cập Revit
    var resp = await server.Post(...);            // ❌ View không gọi network
}
```
View chỉ wire button → set `DialogResult`. VM chỉ giữ state + `Validate()`. Tất cả Revit/Server call ở Command sau khi `ShowDialog() == true`. Đặt logic ở View/VM = không test được + không reuse được khi feature scale.

## Reference

- `Directory.Build.props` — full XML
- `Directory.Packages.props` — CPM versions
- `MEPAuto.sln` — solution structure
- `tools/add-feature/new-feature.ps1` — sinh feature đúng convention (gồm Contract template)
- `tools/add-feature/template-feature/` — csproj template
- `src/client/features/MEPAuto.HelloWorld/` — pilot template
- `docs/workflow/WORKFLOW-NEW-FEATURE.md` — checklist member follow