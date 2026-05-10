# Rule 09 — Feature project structure + Directory.Build.props

> **TL;DR**: 1 feature = 2 project (`MEPAuto.{Feature}` + `MEPAuto.Server.{Feature}`) + 1 file DTO (`{Feature}Dtos.cs` trong Contracts). KHÔNG NuGet — direct project reference. KHÔNG sửa csproj feature ngoài template — TargetFramework / DefineConstants do `Directory.Build.props` quản.

## Monorepo layout

```
MEPAuto/
├── MEPAuto.sln
├── Directory.Build.props           ← MSBuild common — 12 build config
├── Directory.Packages.props        ← CPM (central package management)
│
├── shared/
│   └── MEPAuto.Contracts/          ← .NET Standard 2.0 — wire DTO + IFeatureManifest
│       ├── DTOs/
│       ├── Manifests/IFeatureManifest.cs
│       └── Auth/AuthDtos.cs
│
├── src/
│   ├── client/
│   │   ├── MEPAuto.Client.Shell/
│   │   │   ├── RevitApp.cs
│   │   │   ├── Ribbon/RibbonBuilder.cs
│   │   │   └── Contracts/ContractRegistry.cs
│   │   ├── MEPAuto.Client.Common/
│   │   │   ├── Revit/
│   │   │   ├── Auth/
│   │   │   ├── Commands/BaseFeatureCommand.cs
│   │   │   ├── Contracts/
│   │   │   └── Events/
│   │   └── features/
│   │       ├── MEPAuto.HelloWorld/
│   │       └── MEPAuto.{Feature}/
│   │
│   └── server/
│       ├── MEPAuto.Server.Api/
│       ├── MEPAuto.Server.Core/
│       ├── MEPAuto.Server.Infrastructure.FileSystem/
│       └── features/
│           ├── MEPAuto.Server.HelloWorld/
│           └── MEPAuto.Server.{Feature}/
│
├── tests/
│   └── MEPAuto.Server.Tests/
│
├── tools/
│   ├── deploy/
│   ├── revit-stubs/
│   └── verify-elementid-usage.ps1
│
├── installer/
└── docs/
    ├── rules/
    └── workflow/
```

## Project naming convention

| Loại | Format | Ví dụ |
|---|---|---|
| Client feature | `MEPAuto.{Feature}` | `MEPAuto.DuctRouting` |
| Server feature | `MEPAuto.Server.{Feature}` | `MEPAuto.Server.DuctRouting` |
| DTO file | `shared/MEPAuto.Contracts/DTOs/{Feature}Dtos.cs` | `DuctRoutingDtos.cs` |
| Namespace Client | `MEPAuto.{Feature}.{SubArea}` | `MEPAuto.DuctRouting.Commands` |
| Namespace Server | `MEPAuto.Server.{Feature}.{SubArea}` | `MEPAuto.Server.DuctRouting.Domain` |
| License key | `{feature-lower}.basic` | `ductrouting.basic` |
| Endpoint | `/api/v1/{feature-lower}/...` | `/api/v1/ductrouting/execute` |

## Cấu trúc file 1 feature (chuẩn)

```
src/client/features/MEPAuto.DuctRouting/
├── MEPAuto.DuctRouting.csproj     ProjectRef: Contracts + Client.Common
├── Manifest/DuctRoutingManifest.cs
├── Commands/DuctRoutingCommand.cs  [Transaction] + Execute + ExecuteHeadless
├── Contracts/DuctRoutingContract.cs  IFeatureContract impl
├── Views/                          (optional, khi cần WPF dialog)
│   └── DuctRoutingWindow.xaml(.cs)
└── ViewModels/
    └── DuctRoutingWindowViewModel.cs

src/server/features/MEPAuto.Server.DuctRouting/
├── MEPAuto.Server.DuctRouting.csproj
├── Domain/DuctRoutingLogic.cs     pure function
├── Application/DuctRoutingService.cs
└── Endpoint/DuctRoutingController.cs  [Authorize] + license check

shared/MEPAuto.Contracts/DTOs/
└── DuctRoutingDtos.cs
```

### Vai trò 3 entry point cùng 1 feature

| Entry | Khi nào | UI cho phép? |
|---|---|---|
| `Command.Execute` (User mode) | Ribbon click | ✓ TaskDialog/WPF/PickObject |
| `Command.ExecuteHeadless` | Logic thuần | ✗ KHÔNG show UI |
| `Contract.Execute` | AI / CAD-PDF mode | ✗ KHÔNG show UI |

## Csproj template

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

**KHÔNG khai báo** `<TargetFramework>`, `<DefineConstants>`, Revit reference.

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

## Quy tắc cứng

1. **KHÔNG sửa Directory.Build.props per project**.

2. **KHÔNG `<TargetFrameworks>` (số nhiều)** trong csproj feature.

3. **KHÔNG `<ProjectReference>` giữa 2 feature**.

4. **`<PackageReference>` không version trong feature csproj**: version ở `Directory.Packages.props`.

5. **KHÔNG đổi `OutputPath`**: break copy DLL script.

6. **Feature có WPF dialog → dùng `-WithUi`**. KHÔNG đặt business logic / Revit API call trong code-behind hay VM.

## Anti-pattern ❌

❌ **Copy HelloWorld folder + rename**: bỏ qua sln add + Program.cs DI register.

❌ **TargetFramework cứng trong csproj feature**:
```xml
<TargetFramework>net48</TargetFramework>  <!-- ❌ override Directory.Build.props -->
```

❌ **Bỏ `Contracts/{Feature}Contract.cs`**: AI/CAD-PDF mode không gọi được feature.

❌ **Contract.Execute show TaskDialog / WPF / PickObject** — luồng nền không show được.

❌ **Feature reference Server.Infrastructure direct**:
```xml
<ProjectReference Include="..\..\MEPAuto.Server.Infrastructure.FileSystem\..." />  <!-- ❌ -->
```
Feature chỉ reference `Server.Core` (interface).

## Reference

- `Directory.Build.props` — full XML
- `MEPAuto.sln` — solution structure
- `src/client/features/MEPAuto.HelloWorld/` — pilot template
- `docs/workflow/WORKFLOW-NEW-FEATURE.md`
