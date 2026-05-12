# MEPAuto — Revit MEP Add-in

Monorepo cho add-in MEPAuto, hỗ trợ Revit 2022-2027, kiến trúc VPS-backed (mọi feature qua server).

## Cấu trúc

```
MEPAuto/
├── src/
│   ├── client/                          ← chạy trong Revit (net48 cho 2022-2024, net8.0-windows cho 2025-2027)
│   │   ├── MEPAuto.Client.Shell/        IExternalApplication, ribbon scan, auth bootstrap
│   │   ├── MEPAuto.Client.Common/       IRevitService + ElementIdAdapter + auth helpers
│   │   └── features/
│   │       └── MEPAuto.HelloWorld/      Pilot feature (Manifest + Command)
│   └── server/                          ← chạy trên VPS (net8.0)
│       ├── MEPAuto.Server.Api/          ASP.NET Core 8 entry
│       ├── MEPAuto.Server.Core/         Abstractions (IUserRepository, ILicenseService, ...)
│       ├── MEPAuto.Server.Infrastructure.FileSystem/  Phase 1 JSON file impls
│       └── features/
│           └── MEPAuto.Server.HelloWorld/ Pilot endpoint
├── shared/MEPAuto.Contracts/            DTO + IFeatureManifest (.NET Standard 2.0)
├── tests/                               xUnit (Server + Client integration với FakeRevitService)
├── installer/                           WiX 4 MSI ship 6 version
├── tools/                               CI scripts, deploy, lint
└── .github/workflows/                   CI matrix 6 version
```

## Build (multi-version)

```bash
# Build cho từng version Revit (2022..2027)
dotnet build MEPAuto.sln -c Release-2024
dotnet build MEPAuto.sln -c Release-2025

# Server stack (không phụ thuộc Revit version)
dotnet build src/server/MEPAuto.Server.Api/

# Lint
powershell -ExecutionPolicy Bypass -File tools/verify-elementid-usage.ps1
```

## Deploy VPS

MEPAuto chia chung VPS với EPAuto. Default `deploy.sh` chạy variant `system` (nginx system-site). Xem `tools/deploy/DEPLOY-WALKTHROUGH.md` + `VPS-INVENTORY.md`.

```powershell
# Sync code + trigger deploy
pwsh tools/deploy/sync-m2-to-vps.ps1
```

## Documentation

### Rules — đọc TRƯỚC khi viết code

- [docs/rules/01-architecture.md](docs/rules/01-architecture.md) — Client thin / Server full / Contracts wire format
- [docs/rules/02-multi-version.md](docs/rules/02-multi-version.md) — 12 build config 6 version, ElementIdAdapter, RevitAPI hint path
- [docs/rules/03-security.md](docs/rules/03-security.md) — JWT + license + audit + online enforcement
- [docs/rules/04-irevitservice.md](docs/rules/04-irevitservice.md) — Interface contract, FakeRevitService cho test
- [docs/rules/05-anti-patterns.md](docs/rules/05-anti-patterns.md) — 9 patterns đã gây bug thực tế, ĐỪNG LÀM
- [docs/rules/06-storage-phase.md](docs/rules/06-storage-phase.md) — Phase 1 JSON ↔ Phase 2 Postgres swap
- [docs/rules/07-contract-and-datastorage.md](docs/rules/07-contract-and-datastorage.md) — DTO 4 loại + IDataStorageService cross-feature
- [docs/rules/08-external-event-and-callbacks.md](docs/rules/08-external-event-and-callbacks.md) — ExternalEvent cho background → Revit API
- [docs/rules/09-feature-project-structure.md](docs/rules/09-feature-project-structure.md) — Monorepo layout + Directory.Build.props + naming

### Workflow — cẩm nang member daily

- [docs/workflow/MEMBER-DEV-WORKFLOW.md](docs/workflow/MEMBER-DEV-WORKFLOW.md) — 7-step rebuild → reload → test (15-30s/iteration)
- [docs/workflow/WORKFLOW-NEW-FEATURE.md](docs/workflow/WORKFLOW-NEW-FEATURE.md) — 12-step checklist tạo feature mới từ A-Z
- [docs/workflow/WORKFLOW-REFACTOR.md](docs/workflow/WORKFLOW-REFACTOR.md) — refactor add-in cũ vào monorepo
- [docs/workflow/RIBBON-CONVENTIONS.md](docs/workflow/RIBBON-CONVENTIONS.md) — PanelGroup + Order + DisplayName naming

### Tools

- [tools/add-feature/new-feature.ps1](tools/add-feature/new-feature.ps1) — sinh feature project skeleton
- [tools/dev-setup/install-revit-addin-manager.ps1](tools/dev-setup/install-revit-addin-manager.ps1) — verify + install RAM cho hot-reload
- [tools/dev-setup/test-prompts.md](tools/dev-setup/test-prompts.md) — 3 template prompt MCP auto-test
- [tools/deploy/DEPLOY-WALKTHROUGH.md](tools/deploy/DEPLOY-WALKTHROUGH.md) — 7-step deploy VPS Phase 1
