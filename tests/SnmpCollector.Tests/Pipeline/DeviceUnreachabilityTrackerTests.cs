using SnmpCollector.Pipeline;
using Xunit;

namespace SnmpCollector.Tests.Pipeline;

/// <summary>
/// Unit tests for <see cref="DeviceUnreachabilityTracker"/> covering all state transition paths:
/// below-threshold no-transition, at-threshold transition, above-threshold no-duplicate,
/// success on healthy device, success after unreachable, re-unreachable after recovery,
/// independent device state, and case-insensitive device name lookup.
///
/// Uses direct instantiation — no DI required. xUnit creates a fresh instance per test,
/// giving each test a clean ConcurrentDictionary state.
/// </summary>
public sealed class DeviceUnreachabilityTrackerTests
{
    private readonly DeviceUnreachabilityTracker _tracker = new();

    // -------------------------------------------------------------------------
    // 1. Below threshold: two failures do NOT transition; count is accurate
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailure_BelowThreshold_ReturnsFalse()
    {
        // Act
        var first  = _tracker.RecordFailure("device-a");
        var second = _tracker.RecordFailure("device-a");

        // Assert — no transition below threshold
        Assert.False(first);
        Assert.False(second);
        Assert.Equal(2, _tracker.GetFailureCount("device-a"));
        Assert.False(_tracker.IsUnreachable("device-a"));
    }

    // -------------------------------------------------------------------------
    // 2. At threshold (3rd consecutive failure): returns true on transition
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailure_AtThreshold_ReturnsTrue()
    {
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");

        // 3rd failure — transition to unreachable
        var third = _tracker.RecordFailure("device-a");

        Assert.True(third);
        Assert.True(_tracker.IsUnreachable("device-a"));
    }

    // -------------------------------------------------------------------------
    // 3. Above threshold: 4th failure returns false (already unreachable — no duplicate transition)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordFailure_AboveThreshold_ReturnsFalse()
    {
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        var third  = _tracker.RecordFailure("device-a"); // transitions
        var fourth = _tracker.RecordFailure("device-a"); // already unreachable — no re-transition

        Assert.True(third);
        Assert.False(fourth);
        Assert.Equal(4, _tracker.GetFailureCount("device-a"));
    }

    // -------------------------------------------------------------------------
    // 4. RecordSuccess on healthy device returns false (no transition)
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordSuccess_WhenHealthy_ReturnsFalse()
    {
        // No prior failures
        var result = _tracker.RecordSuccess("device-a");

        Assert.False(result);
        Assert.Equal(0, _tracker.GetFailureCount("device-a"));
        Assert.False(_tracker.IsUnreachable("device-a"));
    }

    // -------------------------------------------------------------------------
    // 5. RecordSuccess after unreachable: transitions to recovered, resets count
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordSuccess_WhenUnreachable_ReturnsTrue()
    {
        // Drive to unreachable
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");

        Assert.True(_tracker.IsUnreachable("device-a"));

        // First success after unreachable — recovery transition
        var recovered = _tracker.RecordSuccess("device-a");

        Assert.True(recovered);
        Assert.False(_tracker.IsUnreachable("device-a"));
        Assert.Equal(0, _tracker.GetFailureCount("device-a"));
    }

    // -------------------------------------------------------------------------
    // 6. Recovery then re-unreachable: failure counter resets; 3 more failures re-trigger
    // -------------------------------------------------------------------------

    [Fact]
    public void RecordSuccess_ResetsCount_AllowsReUnreachable()
    {
        // First unreachable cycle
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        _tracker.RecordSuccess("device-a"); // recover

        // Second unreachable cycle
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        var thirdAgain = _tracker.RecordFailure("device-a"); // re-transition

        Assert.True(thirdAgain);
        Assert.True(_tracker.IsUnreachable("device-a"));
    }

    // -------------------------------------------------------------------------
    // 7. Two different devices maintain independent failure counters
    // -------------------------------------------------------------------------

    [Fact]
    public void IndependentDevices_SeparateState()
    {
        // Drive device-a to unreachable (3 failures)
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");
        _tracker.RecordFailure("device-a");

        // Only 1 failure for device-b
        _tracker.RecordFailure("device-b");

        Assert.True(_tracker.IsUnreachable("device-a"));
        Assert.False(_tracker.IsUnreachable("device-b"));
        Assert.Equal(3, _tracker.GetFailureCount("device-a"));
        Assert.Equal(1, _tracker.GetFailureCount("device-b"));
    }

    // -------------------------------------------------------------------------
    // 8. Device name lookup is case-insensitive (OrdinalIgnoreCase dictionary)
    // -------------------------------------------------------------------------

    [Fact]
    public void DeviceName_CaseInsensitive()
    {
        // Record failure with uppercase name
        _tracker.RecordFailure("DEVICE-A");

        // Retrieve count with lowercase name — must resolve to same entry
        Assert.Equal(1, _tracker.GetFailureCount("device-a"));

        // IsUnreachable also case-insensitive
        Assert.False(_tracker.IsUnreachable("device-a")); // 1 failure, not yet unreachable
    }
}
