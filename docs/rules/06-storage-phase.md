# Rule 06 — Storage Phase Boundary (Phase 1 JSON ↔ Phase 2 Postgres)

> **TL;DR**: Phase 1 dùng JSON file ở `/var/mepauto-data/` (đủ ≤ 50 user). Phase 2 swap sang Postgres + Redis khi có 10k+ user. Tất cả đứng sau interface — Application/Domain code KHÔNG đổi khi swap.

## Phase 1 — JSON file (hiện tại)

| Resource | Implementation | Path |
|---|---|---|
| User | `JsonFileUserRepository` | `/var/mepauto-data/users.json` |
| License | `JsonFileLicenseService` | `/var/mepauto-data/licenses.json` |
| Generic kv (refresh token, session) | `JsonFileDataStorageService` | `/var/mepauto-data/storage/{key}.json` |
| In-process cache | `MemoryCacheService` | RAM |
| Audit log | `FileAuditLogger` | `/var/mepauto-data/audit.log` (JSON-line append) |

### Tại sao JSON file (không SQLite)

- LEAD entry-level — không bắt buộc học EF Core ngay
- ≤ 50 user → I/O file đủ nhanh
- Backup = `tar -czf backup.tar.gz /var/mepauto-data` đơn giản
- Khi cần query phức tạp / concurrent write nhiều → chuyển Phase 2

### Limit Phase 1

- Concurrent write có lock file (SemaphoreSlim) — fine cho ≤ 10 req/s
- Không có index — scan full mảng (O(n))
- Single-server deploy

## Phase 2 — Postgres + Redis (sau, khi đủ user)

| Resource | Implementation | Backend |
|---|---|---|
| User | `PostgresUserRepository` (EF Core) | Postgres `users` table |
| License | `PostgresLicenseService` (EF Core) | Postgres `licenses` table |
| Generic kv | `RedisDataStorageService` | Redis |
| Cache | `RedisCacheService` | Redis |
| Audit log | `PostgresAuditLogger` | Postgres `audit_log` table |

## Quy tắc cứng

1. **Application/Domain code KHÔNG depend implementation**. Chỉ depend abstraction trong `Server.Core/Abstractions/`:
   - `IUserRepository`
   - `ILicenseService`
   - `IDataStorageService`
   - `ICacheService`
   - `IAuditLogger`

2. **Khi viết feature mới**, KHÔNG đụng tới JSON file path direct. Inject interface qua DI:
   ```csharp
   public class DuctRoutingService {
       private readonly IDataStorageService _storage;  // ✅
       // KHÔNG: private readonly string _jsonPath = "/var/mepauto-data/duct.json";  ❌
   }
   ```

3. **DI registration tách biệt** ở `Program.cs` — swap Phase 1/2 chỉ thay 1 chỗ:
   ```csharp
   // Phase 1
   builder.Services.AddSingleton<IUserRepository>(sp => new JsonFileUserRepository(...));

   // Phase 2
   builder.Services.AddDbContext<MEPAutoDb>(opts => opts.UseNpgsql(connStr));
   builder.Services.AddScoped<IUserRepository, PostgresUserRepository>();
   ```

## Khi nào trigger Phase 2

- User count > 100
- Audit log > 100MB/ngày
- Concurrent active user > 20
- Cần query phức tạp
- Multi-region deploy

## Anti-pattern ❌

❌ **Hardcode path JSON file**:
```csharp
public void Save(...) { File.WriteAllText("/var/mepauto-data/duct.json", ...); }  // ❌
```
Phải qua `IDataStorageService.Set("duct/...", obj)`.

❌ **Test Application với JSON file thật**:
Test với `Mock<IUserRepository>` (Moq) hoặc in-memory fake.

## Reference

- `src/server/MEPAuto.Server.Core/Abstractions/` — 5 interface
- `src/server/MEPAuto.Server.Infrastructure.FileSystem/` — Phase 1 impls
- `src/server/MEPAuto.Server.Api/Program.cs` — DI registration
