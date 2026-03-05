using Microsoft.Extensions.Options;

namespace SnmpCollector.Tests.Helpers;

/// <summary>
/// In-memory IOptionsMonitor&lt;T&gt; that allows tests to trigger OnChange callbacks
/// directly, simulating appsettings hot-reload without a running IHost.
/// </summary>
internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private Action<T, string?>? _listener;

    public TestOptionsMonitor(T initial) => CurrentValue = initial;

    public T CurrentValue { get; private set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listener = listener;
        return null;
    }

    /// <summary>
    /// Updates CurrentValue and fires the registered OnChange listener,
    /// simulating an appsettings reload.
    /// </summary>
    public void Change(T newValue)
    {
        CurrentValue = newValue;
        _listener?.Invoke(newValue, null);
    }
}
