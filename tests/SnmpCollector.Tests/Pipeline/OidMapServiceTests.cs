using Microsoft.Extensions.Logging.Abstractions;
using SnmpCollector.Configuration;
using SnmpCollector.Pipeline;
using SnmpCollector.Tests.Helpers;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

public sealed class OidMapServiceTests
{
    private static OidMapService CreateService(Dictionary<string, string> entries)
    {
        var options = new OidMapOptions { Entries = entries };
        var monitor = new TestOptionsMonitor<OidMapOptions>(options);
        return new OidMapService(monitor, NullLogger<OidMapService>.Instance);
    }

    [Fact]
    public void Resolve_KnownOid_ReturnsMetricName()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal("hrProcessorLoad", result);
    }

    [Fact]
    public void Resolve_UnknownOid_ReturnsUnknown()
    {
        var sut = CreateService(new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        });

        var result = sut.Resolve("1.3.6.1.999.999");

        Assert.Equal(OidMapService.Unknown, result);
    }

    [Fact]
    public void Resolve_EmptyMap_AlwaysReturnsUnknown()
    {
        var sut = CreateService(new Dictionary<string, string>());

        var result = sut.Resolve("1.3.6.1.2.1.1.1.0");

        Assert.Equal(OidMapService.Unknown, result);
    }

    [Fact]
    public void EntryCount_MatchesDictionarySize()
    {
        var entries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.1.3.0"] = "sysUpTime",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        var sut = CreateService(entries);

        Assert.Equal(3, sut.EntryCount);
    }

    [Fact]
    public void Resolve_AfterReload_NewOidResolves()
    {
        var initialEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        };
        var options = new OidMapOptions { Entries = initialEntries };
        var monitor = new TestOptionsMonitor<OidMapOptions>(options);
        var sut = new OidMapService(monitor, NullLogger<OidMapService>.Instance);

        // Simulate hot-reload adding a new entry
        var updatedEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        monitor.Change(new OidMapOptions { Entries = updatedEntries });

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal("hrProcessorLoad", result);
    }

    [Fact]
    public void Resolve_AfterReload_RemovedOidReturnsUnknown()
    {
        var initialEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr",
            ["1.3.6.1.2.1.25.3.3.1.2"] = "hrProcessorLoad"
        };
        var options = new OidMapOptions { Entries = initialEntries };
        var monitor = new TestOptionsMonitor<OidMapOptions>(options);
        var sut = new OidMapService(monitor, NullLogger<OidMapService>.Instance);

        // Simulate hot-reload removing an entry
        var updatedEntries = new Dictionary<string, string>
        {
            ["1.3.6.1.2.1.1.1.0"] = "sysDescr"
        };
        monitor.Change(new OidMapOptions { Entries = updatedEntries });

        var result = sut.Resolve("1.3.6.1.2.1.25.3.3.1.2");

        Assert.Equal(OidMapService.Unknown, result);
    }
}
