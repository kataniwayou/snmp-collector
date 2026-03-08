using System.Threading.Channels;

namespace Simetra.Pipeline;

/// <summary>
/// Manages per-device bounded channels for routing trap envelopes from the
/// listener (writer) to Layer 3 consumers (readers).
/// </summary>
public interface IDeviceChannelManager
{
    /// <summary>
    /// Gets the channel writer for a specific device.
    /// </summary>
    /// <param name="deviceName">The registered device name.</param>
    /// <returns>The channel writer for the device.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the device is not registered.</exception>
    ChannelWriter<TrapEnvelope> GetWriter(string deviceName);

    /// <summary>
    /// Gets the channel reader for a specific device. Layer 3 consumers read from this.
    /// </summary>
    /// <param name="deviceName">The registered device name.</param>
    /// <returns>The channel reader for the device.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the device is not registered.</exception>
    ChannelReader<TrapEnvelope> GetReader(string deviceName);

    /// <summary>
    /// Gets all registered device names that have channels.
    /// </summary>
    IReadOnlyCollection<string> DeviceNames { get; }

    /// <summary>
    /// Signals all channel writers as complete (no more writes accepted).
    /// Used during graceful shutdown to initiate channel draining.
    /// </summary>
    void CompleteAll();

    /// <summary>
    /// Awaits <see cref="System.Threading.Channels.ChannelReader{T}.Completion"/> on all
    /// channels. Returns when all channels have been fully drained by their consumers.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the wait.</param>
    Task WaitForDrainAsync(CancellationToken cancellationToken);
}
