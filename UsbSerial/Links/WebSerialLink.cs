using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace UsbSerial.Links;

/// <summary>
/// A serial port through the browser's Web Serial API. The operating system's own driver
/// handles the USB-to-serial chip, so this works with any adapter the browser lists.
/// </summary>
public sealed class WebSerialLink : ISerialLink
{
    readonly SerialPort port;
    WritableStreamDefaultWriter? writer;
    ReadableStreamDefaultReader? reader;
    Task? readLoop;
    bool open;

    WebSerialLink(SerialPort port, string name)
    {
        this.port = port;
        DeviceName = name;
    }

    public string Transport => "Web Serial";
    public string DeviceName { get; }
    public bool Connected => open;
    public event Action<byte[]>? OnData;
    public event Action? OnDisconnect;

    /// <summary>True when this browser has Web Serial at all.</summary>
    public static bool IsSupported(SpawnJSRuntime js)
    {
        using var serial = js.Get<Serial?>("navigator.serial");
        return serial != null;
    }

    /// <summary>
    /// Shows the browser's port chooser (it must run from a click) and opens the port the user picks.
    /// Null when the user cancels or the list was empty.
    /// </summary>
    public static async Task<WebSerialLink?> RequestAsync(SpawnJSRuntime js, int baudRate)
    {
        using var serial = js.Get<Serial>("navigator.serial");
        SerialPort port;
        try
        {
            port = await serial.RequestPort();
        }
        catch (Exception)
        {
            return null; // cancelled, or nothing to choose
        }
        var info = port.GetInfo();
        var name = info.UsbVendorId is { } v
            ? $"USB {v:X4}:{info.UsbProductId ?? 0:X4}"
            : "serial port";
        var link = new WebSerialLink(port, name);
        await link.OpenAsync(baudRate);
        return link;
    }

    async Task OpenAsync(int baudRate)
    {
        await port.Open(new SerialOptions { BaudRate = baudRate, DataBits = 8, StopBits = 1, Parity = "none" });
        port.OnDisconnect += Port_OnDisconnect;
        writer = port.Writable.GetWriter();
        open = true;
        readLoop = ReadLoopAsync();
    }

    async Task ReadLoopAsync()
    {
        while (open && port.Readable is { } readable)
        {
            reader = readable.GetReader();
            try
            {
                while (true)
                {
                    using var result = await reader.Read();
                    if (result.Done) break;
                    if (result.Value is { } chunk) OnData?.Invoke(chunk.ToArray());
                }
            }
            catch
            {
                // a framing or parity error ends this reader; the loop gets a new one
            }
            finally
            {
                try { reader.ReleaseLock(); } catch { }
            }
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (writer == null) throw new InvalidOperationException("Not connected.");
        await writer.Ready;
        using var bytes = new Uint8Array(data);
        await writer.Write(bytes);
    }

    void Port_OnDisconnect(Event e) => _ = CloseAsync();

    public async Task CloseAsync()
    {
        if (!open) return;
        open = false;
        try { await reader!.Cancel(); } catch { }
        if (readLoop != null) await readLoop;
        try { writer?.ReleaseLock(); } catch { }
        writer?.Dispose();
        writer = null;
        port.OnDisconnect -= Port_OnDisconnect;
        try { await port.Close(); } catch { }
        OnDisconnect?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        port.Dispose();
    }
}
