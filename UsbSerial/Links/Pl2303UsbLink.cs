using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace UsbSerial.Links;

/// <summary>
/// A Prolific PL2303 USB-to-serial adapter driven directly over WebUSB: a small driver in C#.
/// Use it where Web Serial does not list the adapter (we found that on Android).
///
/// The PL2303 is not a standard (CDC-ACM) USB serial device: it needs Prolific's own vendor
/// requests before it will pass data, which is why Google's Web Serial polyfill cannot drive it.
/// The initialization sequence below follows pl2303-webusb (MIT, see THIRD-PARTY-NOTICES.md),
/// as SpawnDev.NexStar does to talk to Celestron telescopes.
/// </summary>
public sealed class Pl2303UsbLink : ISerialLink
{
    /// <summary>Prolific Technology's USB vendor id.</summary>
    public const ushort ProlificVendorId = 0x067B;

    const byte SetLineCoding = 0x20;
    const byte SetControlLineState = 0x22;

    readonly SpawnJSRuntime js;
    readonly USBDevice device;
    USB? usb;
    byte inEndpoint, outEndpoint;
    CancellationTokenSource? stop;
    Task? readLoop;
    bool open;

    Pl2303UsbLink(SpawnJSRuntime js, USBDevice device)
    {
        this.js = js;
        this.device = device;
        DeviceName = device.ProductName ?? "PL2303";
    }

    public string Transport => "WebUSB (PL2303)";
    public string DeviceName { get; }
    public bool Connected => open;
    public event Action<byte[]>? OnData;
    public event Action? OnDisconnect;

    /// <summary>True when this browser has WebUSB at all.</summary>
    public static bool IsSupported(SpawnJSRuntime js)
    {
        using var usb = js.Get<USB?>("navigator.usb");
        return usb != null;
    }

    /// <summary>
    /// Shows the browser's USB chooser, filtered to Prolific adapters (it must run from a click),
    /// and opens the one the user picks. Null when the user cancels.
    /// </summary>
    public static async Task<Pl2303UsbLink?> RequestAsync(SpawnJSRuntime js, int baudRate)
    {
        using var usb = js.Get<USB>("navigator.usb");
        USBDevice device;
        try
        {
            device = await usb.RequestDevice(new() { Filters = [new USBDeviceFilter { VendorId = ProlificVendorId }] });
        }
        catch (Exception)
        {
            return null; // cancelled, or no Prolific adapter plugged in
        }
        var link = new Pl2303UsbLink(js, device);
        await link.OpenAsync(baudRate);
        return link;
    }

    async Task OpenAsync(int baudRate)
    {
        await device.Open();
        await device.SelectConfiguration(1);
        var iface = device.Configuration!.Interfaces[0];
        await device.ClaimInterface(iface.InterfaceNumber);

        // Prolific's start-up sequence (vendor requests), from pl2303-webusb.
        await VendorRead(0x8484, 0);
        await VendorWrite(0x0404, 0);
        await VendorRead(0x8484, 0);
        await VendorRead(0x8383, 0);
        await VendorRead(0x8484, 0);
        await VendorWrite(0x0404, 1);
        await VendorRead(0x8484, 0);
        await VendorRead(0x8383, 0);
        await VendorWrite(0, 1);
        await VendorWrite(1, 0);
        await VendorWrite(2, 0x44);

        // Line settings: baud rate (little-endian), 1 stop bit, no parity, 8 data bits.
        byte[] coding = [(byte)baudRate, (byte)(baudRate >> 8), (byte)(baudRate >> 16), (byte)(baudRate >> 24), 0, 0, 8];
        using (var codingBytes = new Uint8Array(coding))
            await device.ControlTransferOut(Class(SetLineCoding, 0), codingBytes);

        await VendorWrite(0, 0); // no flow control
        await VendorWrite(8, 0); // reset the data pipes
        await VendorWrite(9, 0);

        // Raise DTR and RTS: many devices wait for them before they talk.
        await device.ControlTransferOut(Class(SetControlLineState, 0x03));

        foreach (var endpoint in iface.Alternates[0].Endpoints.ToArray())
        {
            if (endpoint.Type != "bulk") continue;
            if (endpoint.Direction == "in") inEndpoint = (byte)endpoint.EndpointNumber;
            else outEndpoint = (byte)endpoint.EndpointNumber;
        }
        if (inEndpoint == 0 || outEndpoint == 0)
            throw new InvalidOperationException("The PL2303's bulk endpoints were not found.");

        usb = js.Get<USB>("navigator.usb");
        usb.OnDisconnect += Usb_OnDisconnect;
        open = true;
        stop = new CancellationTokenSource();
        readLoop = ReadLoopAsync(stop.Token);
    }

    async Task ReadLoopAsync(CancellationToken token)
    {
        while (open && !token.IsCancellationRequested)
        {
            try
            {
                using var result = await device.TransferIn(inEndpoint, 64);
                if (result.Status == "ok" && result.Data is { } data)
                {
                    using (data)
                    {
                        var bytes = data.ReadBytes();
                        if (bytes.Length > 0) OnData?.Invoke(bytes);
                    }
                }
            }
            catch
            {
                if (!open) break;
                await Task.Delay(50);
            }
        }
    }

    public async Task WriteAsync(byte[] data)
    {
        if (!open) throw new InvalidOperationException("Not connected.");
        using var bytes = new Uint8Array(data);
        await device.TransferOut(outEndpoint, bytes);
    }

    void Usb_OnDisconnect(USBConnectionEvent e)
    {
        using var gone = e.Device;
        if (gone.VendorId == device.VendorId && gone.ProductId == device.ProductId && gone.SerialNumber == device.SerialNumber)
            _ = CloseAsync();
    }

    public async Task CloseAsync()
    {
        if (!open) return;
        open = false;
        stop?.Cancel();
        // Closing the device ends the pending TransferIn; waiting for the loop first would wait for bytes that may never come.
        try { await device.Close(); } catch { }
        if (readLoop != null) await Task.WhenAny(readLoop, Task.Delay(2000));
        if (usb != null)
        {
            usb.OnDisconnect -= Usb_OnDisconnect;
            usb.Dispose();
            usb = null;
        }
        stop?.Dispose();
        stop = null;
        OnDisconnect?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        device.Dispose();
    }

    static USBControlTransferParameters Class(byte request, int value) =>
        new() { RequestType = "class", Recipient = "interface", Request = request, Value = value, Index = 0 };

    static USBControlTransferParameters Vendor(int value, int index) =>
        new() { RequestType = "vendor", Recipient = "device", Request = 0x01, Value = value, Index = index };

    async Task VendorWrite(int value, int index) => await device.ControlTransferOut(Vendor(value, index));

    async Task VendorRead(int value, int index)
    {
        using var _ = await device.ControlTransferIn(Vendor(value, index), 1);
    }
}
