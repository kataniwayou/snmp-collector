using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Simetra.Configuration;
using Simetra.Devices;

namespace Simetra.Pipeline;

/// <summary>
/// Singleton that creates and manages one bounded <see cref="Channel{TrapEnvelope}"/>
/// per device (from both configuration and code-defined modules). Channels use
/// <see cref="BoundedChannelFullMode.DropOldest"/> with a Debug-level log callback
/// when items are dropped.
/// </summary>
public sealed class DeviceChannelManager : IDeviceChannelManager
{
    private readonly Dictionary<string, Channel<TrapEnvelope>> _channels;
    private readonly ILogger<DeviceChannelManager> _logger;

    /// <summary>
    /// Initializes a new instance, creating one bounded channel per device from
    /// configuration and from any <see cref="IVirtualDeviceModule"/> instances.
    /// </summary>
    /// <param name="devicesOptions">Device configurations defining which channels to create.</param>
    /// <param name="channelsOptions">Channel capacity configuration.</param>
    /// <param name="modules">Device modules; virtual modules get auto-created channels.</param>
    /// <param name="logger">Logger for item-dropped callbacks and lifecycle operations.</param>
    public DeviceChannelManager(
        IOptions<DevicesOptions> devicesOptions,
        IOptions<ChannelsOptions> channelsOptions,
        IEnumerable<IDeviceModule> modules,
        ILogger<DeviceChannelManager> logger)
    {
        _logger = logger;
        var capacity = channelsOptions.Value.BoundedCapacity;
        _channels = new Dictionary<string, Channel<TrapEnvelope>>(StringComparer.Ordinal);

        // Collect all device names: config devices + virtual device modules
        var deviceNames = devicesOptions.Value.Devices
            .Select(d => d.Name)
            .Concat(modules.OfType<IVirtualDeviceModule>().Select(vm => vm.VirtualDeviceName))
            .Distinct(StringComparer.Ordinal);

        foreach (var deviceName in deviceNames)
        {
            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = false,
                SingleReader = true,
                AllowSynchronousContinuations = false
            };

            var name = deviceName;
            var channel = Channel.CreateBounded(options, (TrapEnvelope dropped) =>
            {
                logger.LogDebug(
                    "Trap dropped from device {DeviceName} channel (capacity {Capacity})",
                    name,
                    capacity);
            });

            _channels[deviceName] = channel;
        }

    }

    /// <inheritdoc />
    public ChannelWriter<TrapEnvelope> GetWriter(string deviceName)
    {
        return _channels[deviceName].Writer;
    }

    /// <inheritdoc />
    public ChannelReader<TrapEnvelope> GetReader(string deviceName)
    {
        return _channels[deviceName].Reader;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DeviceNames => _channels.Keys;

    /// <inheritdoc />
    public void CompleteAll()
    {
        foreach (var (deviceName, channel) in _channels)
        {
            try
            {
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to complete channel writer for device {DeviceName}",
                    deviceName);
            }
        }

        _logger.LogInformation("Completed all {Count} device channel writers", _channels.Count);
    }

    /// <inheritdoc />
    public async Task WaitForDrainAsync(CancellationToken cancellationToken)
    {
        var completionTasks = _channels.Values
            .Select(channel => channel.Reader.Completion)
            .ToList();

        await Task.WhenAll(completionTasks).WaitAsync(cancellationToken);

        _logger.LogInformation("All device channels drained");
    }
}
