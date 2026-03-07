using Microsoft.Extensions.Configuration;
using SnmpCollector.Configuration;
using System.Text.RegularExpressions;
using Xunit;

namespace SnmpCollector.Tests.Configuration;

/// <summary>
/// Integration tests verifying OID map auto-scan: JSONC parsing, multi-file merge,
/// OBP entry count, naming convention, and OID prefix consistency.
/// </summary>
public class OidMapAutoScanTests
{
    /// <summary>
    /// Locates the real oidmap-obp.json relative to the test assembly output directory.
    /// Path: {testBin}/../../../../src/SnmpCollector/config/oidmap-obp.json
    /// </summary>
    private static string GetOidMapPath()
    {
        var testDir = Path.GetDirectoryName(typeof(OidMapAutoScanTests).Assembly.Location)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "src", "SnmpCollector", "config", "oidmap-obp.json");
    }

    /// <summary>
    /// Binds the OidMap section from an IConfiguration into OidMapOptions.Entries,
    /// matching the production binding pattern in ServiceCollectionExtensions.
    /// </summary>
    private static OidMapOptions BindOidMapOptions(IConfiguration config)
    {
        var options = new OidMapOptions();
        config.GetSection(OidMapOptions.SectionName).Bind(options.Entries);
        return options;
    }

    [Fact]
    public void LoadsOidMapFromJsoncFile()
    {
        // Arrange: create a temp JSONC file with // comments
        var tempFile = Path.Combine(Path.GetTempPath(), $"oidmap-test-{Guid.NewGuid()}.json");
        try
        {
            var jsonc = """
                {
                  // This is a JSONC comment -- must not cause parse errors
                  "OidMap": {
                    // CPU load OID
                    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad",
                    // Uptime OID
                    "1.3.6.1.2.1.1.3.0": "sysUpTime"
                  }
                }
                """;
            File.WriteAllText(tempFile, jsonc);

            // Act
            var config = new ConfigurationBuilder()
                .AddJsonFile(tempFile, optional: false, reloadOnChange: false)
                .Build();

            var options = BindOidMapOptions(config);

            // Assert: comments did not cause errors and entries are present
            Assert.Equal(2, options.Entries.Count);
            Assert.Equal("hrProcessorLoad", options.Entries["1.3.6.1.2.1.25.3.3.1.2"]);
            Assert.Equal("sysUpTime", options.Entries["1.3.6.1.2.1.1.3.0"]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MergesMultipleOidMapFiles()
    {
        // Arrange: two separate JSONC files with different OID entries under "OidMap"
        var tempFile1 = Path.Combine(Path.GetTempPath(), $"oidmap-merge1-{Guid.NewGuid()}.json");
        var tempFile2 = Path.Combine(Path.GetTempPath(), $"oidmap-merge2-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile1, """
                {
                  "OidMap": {
                    "1.3.6.1.2.1.25.3.3.1.2": "hrProcessorLoad"
                  }
                }
                """);

            File.WriteAllText(tempFile2, """
                {
                  "OidMap": {
                    "1.3.6.1.2.1.1.3.0": "sysUpTime",
                    "1.3.6.1.2.1.2.2.1.10": "ifInOctets"
                  }
                }
                """);

            // Act: load both files (simulating auto-scan merge)
            var config = new ConfigurationBuilder()
                .AddJsonFile(tempFile1, optional: false, reloadOnChange: false)
                .AddJsonFile(tempFile2, optional: false, reloadOnChange: false)
                .Build();

            var options = BindOidMapOptions(config);

            // Assert: entries from BOTH files are merged
            Assert.Equal(3, options.Entries.Count);
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.25.3.3.1.2"), "File 1 entry missing");
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.1.3.0"), "File 2 entry 1 missing");
            Assert.True(options.Entries.ContainsKey("1.3.6.1.2.1.2.2.1.10"), "File 2 entry 2 missing");
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }

    [Fact]
    public void ObpOidMapHas24Entries()
    {
        // Arrange: load the real oidmap-obp.json from the source config directory
        var path = GetOidMapPath();
        Assert.True(File.Exists(path), $"oidmap-obp.json not found at: {path}");

        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();

        // Act
        var options = BindOidMapOptions(config);

        // Assert: exactly 24 entries (4 links x 6 metrics)
        Assert.Equal(24, options.Entries.Count);

        // Spot-check specific entries across different links and metric types
        Assert.Equal("obp_link_state_L1", options.Entries["1.3.6.1.4.1.47477.10.21.1.3.1.0"]);
        Assert.Equal("obp_r4_power_L4", options.Entries["1.3.6.1.4.1.47477.10.21.4.3.13.0"]);
        Assert.Equal("obp_channel_L2", options.Entries["1.3.6.1.4.1.47477.10.21.2.3.4.0"]);
    }

    [Fact]
    public void ObpOidNamingConventionIsConsistent()
    {
        // Arrange
        var path = GetOidMapPath();
        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();

        var options = BindOidMapOptions(config);

        // Act & Assert: all metric names match obp_{metric}_L{1-4} pattern
        var pattern = new Regex(@"^obp_(link_state|channel|r[1-4]_power)_L[1-4]$");

        foreach (var (oid, metricName) in options.Entries)
        {
            Assert.Matches(pattern, metricName);
        }
    }

    [Fact]
    public void ObpOidStringsFollowEnterprisePrefix()
    {
        // Arrange
        var path = GetOidMapPath();
        var config = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false, reloadOnChange: false)
            .Build();

        var options = BindOidMapOptions(config);

        // Act & Assert: all OID keys start with enterprise prefix and end with .0
        const string enterprisePrefix = "1.3.6.1.4.1.47477.10.21.";

        foreach (var oid in options.Entries.Keys)
        {
            Assert.StartsWith(enterprisePrefix, oid);
            Assert.EndsWith(".0", oid);
        }
    }
}
