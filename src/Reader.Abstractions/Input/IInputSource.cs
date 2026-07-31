namespace OpenReader.Abstractions.Input;

/// <summary>
/// Raw input producer. Platform implementations hook the OS keyboard / touch /
/// braille input and forward events through this interface.
/// </summary>
/// <remarks>
/// Implementations should consume their own thread for hooks where required by
/// the OS and post events into the runtime via <see cref="RawInputReceived"/>.
/// </remarks>
public interface IInputSource : IAsyncDisposable
{
    /// <summary>Fires for each raw input event. Handlers must be fast and non-blocking.</summary>
    event EventHandler<RawInput> RawInputReceived;

    ValueTask StartAsync(CancellationToken cancellationToken);
}
