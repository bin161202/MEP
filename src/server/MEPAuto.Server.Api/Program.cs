using System.IO;
using System.Text;
using MEPAuto.Server.Api.Auth;
using MEPAuto.Server.Api.Commands;
using MEPAuto.Server.Api.Middleware;
using MEPAuto.Server.Core.Abstractions;
using MEPAuto.Server.Core.Auth;
using MEPAuto.Server.Infrastructure.FileSystem;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Formatting.Compact;

// CLI subcommand mode — intercept trước khi build web host.
// Hiện hỗ trợ: seed-user (dev local, không động VPS).
if (args.Length > 0 && args[0] == "seed-user")
{
    return await SeedUserCommand.RunAsync(args.Skip(1).ToArray());
}

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog (Console human-readable + File JSON cho parse via jq/log shipper) ----
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "mepauto-api")
    .WriteTo.Console()
    .WriteTo.File(
        formatter: new CompactJsonFormatter(),
        path: Path.Combine(ctx.Configuration["DataDir"] ?? "/var/mepauto-data", "logs", "server-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30));

// ---- Env var fallback layer ----
static string EnvOrConfig(string envKey, string configKey, IConfiguration cfg, string defaultValue = "")
{
    var v = System.Environment.GetEnvironmentVariable(envKey);
    if (!string.IsNullOrEmpty(v)) return v;
    v = cfg[configKey];
    return string.IsNullOrEmpty(v) ? defaultValue : v;
}

// ---- JWT options + bearer auth ----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = new JwtOptions
{
    SigningKey = EnvOrConfig("JWT__SIGNING_KEY", "Jwt:SigningKey", builder.Configuration),
    Issuer = EnvOrConfig("JWT__ISSUER", "Jwt:Issuer", builder.Configuration, "https://api.mepauto.local"),
    Audience = EnvOrConfig("JWT__AUDIENCE", "Jwt:Audience", builder.Configuration, "mepauto-client"),
    AccessTokenMinutes = int.TryParse(EnvOrConfig("JWT__ACCESSTOKENMINUTES", "Jwt:AccessTokenMinutes", builder.Configuration), out var atm) ? atm : 30,
    RefreshTokenHours = int.TryParse(EnvOrConfig("JWT__REFRESHTOKENHOURS", "Jwt:RefreshTokenHours", builder.Configuration), out var rth) ? rth : 8,
};
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new System.InvalidOperationException(
        $"Jwt:SigningKey không có hoặc < 32 byte. Set qua env JWT__SIGNING_KEY. Hiện length={jwt.SigningKey.Length}.");
builder.Services.PostConfigure<JwtOptions>(o =>
{
    o.SigningKey = jwt.SigningKey;
    o.Issuer = jwt.Issuer;
    o.Audience = jwt.Audience;
    o.AccessTokenMinutes = jwt.AccessTokenMinutes;
    o.RefreshTokenHours = jwt.RefreshTokenHours;
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = System.TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// ---- Storage Phase 1 (JSON file) ----
var dataDir = builder.Configuration["DataDir"] ?? "/var/mepauto-data";
Directory.CreateDirectory(dataDir);

builder.Services.AddSingleton<IUserRepository>(sp => new JsonFileUserRepository(
    Path.Combine(dataDir, "users.json"),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JsonFileUserRepository>>()));
builder.Services.AddSingleton<ILicenseService>(_ => new JsonFileLicenseService(
    Path.Combine(dataDir, "licenses.json")));
builder.Services.AddSingleton<IDataStorageService>(_ => new JsonFileDataStorageService(
    Path.Combine(dataDir, "storage")));
builder.Services.AddSingleton<IAuditLogger>(sp => new FileAuditLogger(
    Path.Combine(dataDir, "audit.log"),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FileAuditLogger>>()));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ICacheService, MemoryCacheService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// In-memory metrics collector (singleton — state cộng dồn từ start).
builder.Services.AddSingleton<MetricsCollector>();

// ---- Feature services (mỗi feature mới thêm 1 dòng AddScoped) ----
builder.Services.AddScoped<MEPAuto.Server.HelloWorld.Application.HelloWorldService>();

// VersionService singleton (cache version.json in-memory 60s).
builder.Services.AddSingleton(sp => new MEPAuto.Server.Versioning.Application.VersionService(
    dataDir,
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MEPAuto.Server.Versioning.Application.VersionService>>()));

// ---- Controllers + Swagger ----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ---- Pipeline ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<MetricsMiddleware>();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.MapControllers();

app.Run();
return 0;
