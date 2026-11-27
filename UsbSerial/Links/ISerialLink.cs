namespace UsbSerial.Links;

/// <summary>
/// A serial connection, whichever browser API carries it. The page only talks to this,
/// so it works the same over Web Serial and over WebUSB.
/// </summary>
public interface ISerialLink : IAsyncDisposable
{
    /// <summary>"Web Serial" or "WebUSB (PL2303)".</summary>
    string Transport { get; }

    /// <summary>What the browser says it connected to.</summary>
    string DeviceName { get; }

    bool Connected { get; }

    /// <summary>Bytes received from the device, as they arrive.</summary>
    event Action<byte[]>? OnData;

    /// <summary>The device went away: unplugged, or closed.</summary>
    event Action? OnDisconnect;

    Task WriteAsync(byte[] data);

    Task CloseAsync();
}
