using System;
using System.IO;
using System.Threading.Tasks;
using MEPAuto.Server.Versioning.Application;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Xunit;

namespace MEPAuto.Server.Tests.Application;

/// <summary>
/// Unit test cho VersionService — verify SemVer compare + version.json parse + Mandatory logic + DownloadUrl render.
/// Mỗi test tự tạo temp dir + cleanup (IDisposable) để isolate filesystem.
/// </summary>
public class VersionServiceTests : IDisposable
{
    private readonly string _tempDir;

    public VersionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MEPAuto-VersionServiceTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private VersionService MakeService() =>
        new VersionService(_tempDir, NullLogger<VersionService>.Instance);

    private void WriteVersionJson(object schema)
    {
        var path = Path.Combine(_tempDir, "version.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(schema));
    }

    // ===== CompareVersion =====

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("0.1.0", "0.10.0", -1)]   // numeric compare, không lexicographic
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.0.0-rc1", "1.0.0", 0)] // strip pre-release suffix
    [InlineData("1.0", "1.0.0", 0)]       // missing segment = 0
    [InlineData("", "1.0.0", 0)]          // empty = ignore (return 0)
    public void CompareVersion_returns_expected_sign(string a, string b, int expectedSign)
    {
        var actual = VersionService.CompareVersion(a, b);
        Math.Sign(actual).Should().Be(expectedSign);
    }

    // ===== GetVersionInfo: file missing =====

    [Fact]
    public async Task GetVersionInfo_file_missing_returns_empty_default()
    {
        var svc = MakeService();
        var info = await svc.GetVersionInfo("0.1.0");

        info.Latest.Should().Be("0.0.0");
        info.MinSupported.Should().Be("0.0.0");
        info.DownloadUrl.Should().BeEmpty();
        info.Sha256.Should().BeEmpty();
        info.Mandatory.Should().BeFalse();
    }

    // ===== GetVersionInfo: file hợp lệ =====

    [Fact]
    public async Task GetVersionInfo_full_schema_returns_all_fields()
    {
        WriteVersionJson(new
        {
            latest = "0.1.5",
            minSupported = "0.1.0",
            downloadUrlPattern = "https://example.com/MEPAuto-{version}.msi",
            sha256ByVersion = new System.Collections.Generic.Dictionary<string, string>
            {
                ["0.1.5"] = "abc123def456",
                ["0.1.4"] = "old_hash",
            },
            releaseNotes = "## 0.1.5\n- Fix bug X",
            revitVersions = new[] { "2024", "2025" },
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("0.1.4");

        info.Latest.Should().Be("0.1.5");
        info.MinSupported.Should().Be("0.1.0");
        info.DownloadUrl.Should().Be("https://example.com/MEPAuto-0.1.5.msi");
        info.Sha256.Should().Be("abc123def456");
        info.ReleaseNotes.Should().Contain("Fix bug X");
        info.RevitVersions.Should().BeEquivalentTo(new[] { "2024", "2025" });
        info.Mandatory.Should().BeFalse();
    }

    // ===== Mandatory logic =====

    [Fact]
    public async Task GetVersionInfo_client_below_minSupported_is_mandatory()
    {
        WriteVersionJson(new
        {
            latest = "0.2.0",
            minSupported = "0.1.5",
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("0.1.0");

        info.Mandatory.Should().BeTrue();
    }

    [Fact]
    public async Task GetVersionInfo_client_at_minSupported_not_mandatory()
    {
        WriteVersionJson(new
        {
            latest = "0.2.0",
            minSupported = "0.1.5",
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("0.1.5");

        info.Mandatory.Should().BeFalse();
    }

    [Fact]
    public async Task GetVersionInfo_empty_current_not_mandatory()
    {
        WriteVersionJson(new
        {
            latest = "0.2.0",
            minSupported = "0.1.5",
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("");

        info.Mandatory.Should().BeFalse();
    }

    // ===== DownloadUrl pattern =====

    [Fact]
    public async Task GetVersionInfo_renders_download_url_pattern_with_version_placeholder()
    {
        WriteVersionJson(new
        {
            latest = "1.2.3",
            downloadUrlPattern = "https://github.com/Org/MEPAuto/releases/download/v{version}/Setup-v{version}.msi",
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("1.0.0");

        info.DownloadUrl.Should().Be("https://github.com/Org/MEPAuto/releases/download/v1.2.3/Setup-v1.2.3.msi");
    }

    [Fact]
    public async Task GetVersionInfo_no_download_pattern_returns_empty_url()
    {
        WriteVersionJson(new
        {
            latest = "0.1.0",
        });

        var svc = MakeService();
        var info = await svc.GetVersionInfo("");

        info.DownloadUrl.Should().BeEmpty();
    }

    // ===== Cache behavior =====

    [Fact]
    public async Task GetVersionInfo_caches_for_60s_does_not_re_read_file()
    {
        WriteVersionJson(new { latest = "0.1.0" });
        var svc = MakeService();
        var info1 = await svc.GetVersionInfo("");
        info1.Latest.Should().Be("0.1.0");

        WriteVersionJson(new { latest = "9.9.9" });
        var info2 = await svc.GetVersionInfo("");

        // Cache 60s → vẫn trả version cũ
        info2.Latest.Should().Be("0.1.0");
    }

    // ===== Malformed JSON =====

    [Fact]
    public async Task GetVersionInfo_malformed_json_returns_default()
    {
        File.WriteAllText(Path.Combine(_tempDir, "version.json"), "{ this is not json");

        var svc = MakeService();
        var info = await svc.GetVersionInfo("0.1.0");

        info.Latest.Should().Be("0.0.0");
    }
}
