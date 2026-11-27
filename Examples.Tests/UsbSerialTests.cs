using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace Examples.Tests;

/// <summary>
/// Runs the PUBLISHED UsbSerial example in a real Chromium. A test cannot plug in a cable or click the
/// browser's device chooser, so navigator.serial and navigator.usb are replaced by loopback devices that
/// echo what they are sent and record every USB request. The C# drivers run unchanged against them.
/// The real adapter (PL2303 on a Celestron telescope) is tested by hand, on a PC and on Android phones.
/// </summary>
[TestClass]
public sealed class UsbSerialTests
{
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;
    static string _url = "";

    // A Web Serial port whose output comes straight back as input.
    const string LoopbackSerial = """
        (() => {
            class LoopbackPort extends EventTarget {
                getInfo() { return { usbVendorId: 0x067B, usbProductId: 0x2303 }; }
                async open(options) {
                    window.serialOpened = options;
                    let input;
                    this.readable = new ReadableStream({ start(c) { input = c; } });
                    this.writable = new WritableStream({ write(chunk) { input.enqueue(new Uint8Array(chunk)); } });
                    this.connected = true;
                }
                async close() { window.serialClosed = true; this.readable = null; this.writable = null; }
                async forget() { }
            }
            const serial = new EventTarget();
            serial.requestPort = async () => new LoopbackPort();
            serial.getPorts = async () => [];
            Object.defineProperty(navigator, 'serial', { value: serial, configurable: true });
        })();
        """;

    // A PL2303 that answers every control request, records it, and echoes bulk OUT data back on bulk IN.
    const string LoopbackPl2303 = """
        (() => {
            window.usbControl = [];
            const pending = [], waiting = [];
            const device = {
                vendorId: 0x067B, productId: 0x2303, productName: 'USB-Serial Controller D', serialNumber: '',
                opened: false,
                configuration: { interfaces: [{ interfaceNumber: 0, alternates: [{ endpoints: [
                    { endpointNumber: 1, direction: 'in', type: 'interrupt' },
                    { endpointNumber: 2, direction: 'out', type: 'bulk' },
                    { endpointNumber: 3, direction: 'in', type: 'bulk' }] }] }] },
                async open() { this.opened = true; },
                async close() { this.opened = false; while (waiting.length) waiting.shift().reject(new DOMException('closed', 'AbortError')); window.usbClosed = true; },
                async selectConfiguration() { },
                async claimInterface() { },
                async controlTransferOut(setup, data) {
                    window.usbControl.push({ dir: 'out', type: setup.requestType, request: setup.request, value: setup.value, index: setup.index, data: data ? [...new Uint8Array(data.buffer ?? data)] : null });
                    return { status: 'ok', bytesWritten: data ? data.byteLength : 0 };
                },
                async controlTransferIn(setup, length) {
                    window.usbControl.push({ dir: 'in', type: setup.requestType, request: setup.request, value: setup.value, index: setup.index });
                    return { status: 'ok', data: new DataView(new ArrayBuffer(length)) };
                },
                async transferOut(endpoint, data) {
                    const bytes = new Uint8Array(data.buffer ?? data).slice();
                    if (waiting.length) waiting.shift().resolve(bytes); else pending.push(bytes);
                    return { status: 'ok', bytesWritten: bytes.length };
                },
                transferIn(endpoint, length) {
                    const wrap = b => ({ status: 'ok', data: new DataView(b.buffer) });
                    if (pending.length) return Promise.resolve(wrap(pending.shift()));
                    return new Promise((resolve, reject) => waiting.push({ resolve: b => resolve(wrap(b)), reject }));
                },
                async forget() { }
            };
            const usb = new EventTarget();
            usb.requestDevice = async () => device;
            usb.getDevices = async () => [];
            Object.defineProperty(navigator, 'usb', { value: usb, configurable: true });
        })();
        """;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _app = await PublishedExample.StartAsync("UsbSerial");
        _url = _app.Urls.First();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync();
    }

    [ClassCleanup]
    public static async Task Cleanup()
    {
        await _browser.DisposeAsync();
        _playwright.Dispose();
        await _app.DisposeAsync();
    }

    static async Task<(IPage Page, List<string> Errors)> Open(string? initScript = null)
    {
        var page = await (await _browser.NewContextAsync()).NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        if (initScript != null) await page.AddInitScriptAsync(initScript);
        await page.GotoAsync(_url);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Serial terminal");
        return (page, errors);
    }

    static async Task SendAndExpectEcho(IPage page, string text)
    {
        await page.FillAsync("#send-text", text);
        await page.ClickAsync("#send");
        await Assertions.Expect(page.Locator("#output")).ToContainTextAsync(text);
    }

    [TestMethod]
    public async Task Page_SaysWhatThisBrowserSupports()
    {
        var (page, errors) = await Open();
        var support = page.Locator("#support");
        await Assertions.Expect(support).ToContainTextAsync("Web Serial available");
        await Assertions.Expect(support).ToContainTextAsync("WebUSB available");
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task Page_DisablesAButton_WhenTheBrowserLacksItsApi()
    {
        // As on a browser without Web Serial (Firefox, Safari): the page must say so, not fail.
        var (page, errors) = await Open("delete Navigator.prototype.serial;");
        await Assertions.Expect(page.Locator("#support")).ToContainTextAsync("Web Serial not available");
        await Assertions.Expect(page.Locator("#web-serial")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#web-usb")).ToBeEnabledAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task WebSerial_OpensAtTheChosenSpeed_EchoesText_AndCloses()
    {
        var (page, errors) = await Open(LoopbackSerial);
        await page.SelectOptionAsync("#baud", "115200");
        await page.ClickAsync("#web-serial");
        await Assertions.Expect(page.Locator("#connected")).ToContainTextAsync("over Web Serial at 115200 baud");
        Assert.AreEqual(115200, await page.EvaluateAsync<int>("() => serialOpened.baudRate"));
        await SendAndExpectEcho(page, "Kx");
        await page.ClickAsync("#disconnect");
        await Assertions.Expect(page.Locator("#status")).ToHaveTextAsync("Disconnected.");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => serialClosed === true"));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task WebUsb_SendsTheProlificSetup_ThenEchoesText_AndCloses()
    {
        var (page, errors) = await Open(LoopbackPl2303);
        await page.SelectOptionAsync("#baud", "9600");
        await page.ClickAsync("#web-usb");
        await Assertions.Expect(page.Locator("#connected")).ToContainTextAsync("USB-Serial Controller D over WebUSB (PL2303) at 9600 baud");

        // The vendor start-up sequence, in order, then the line coding and the control lines.
        var sent = await page.EvaluateAsync<string>("() => usbControl.map(c => c.dir + ' ' + c.type + ' ' + c.request + ' ' + c.value + ' ' + c.index + (c.data ? ' [' + c.data.join(',') + ']' : '')).join('\\n')");
        var expected = string.Join("\n",
            "in vendor 1 33924 0", "out vendor 1 1028 0", "in vendor 1 33924 0", "in vendor 1 33667 0",
            "in vendor 1 33924 0", "out vendor 1 1028 1", "in vendor 1 33924 0", "in vendor 1 33667 0",
            "out vendor 1 0 1", "out vendor 1 1 0", "out vendor 1 2 68",
            "out class 32 0 0 [128,37,0,0,0,0,8]", // 9600 = 0x2580, 1 stop bit, no parity, 8 data bits
            "out vendor 1 0 0", "out vendor 1 8 0", "out vendor 1 9 0",
            "out class 34 3 0"); // DTR and RTS on
        Assert.AreEqual(expected, sent);

        await SendAndExpectEcho(page, "Kx");
        await page.ClickAsync("#disconnect");
        await Assertions.Expect(page.Locator("#status")).ToHaveTextAsync("Disconnected.");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => usbClosed === true"));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task Chooser_Cancelled_SaysWhatToTry()
    {
        var (page, errors) = await Open("""
            Object.defineProperty(navigator, 'serial', { value: Object.assign(new EventTarget(), {
                requestPort: async () => { throw new DOMException('No port selected by the user.', 'NotFoundError'); },
                getPorts: async () => [] }), configurable: true });
            """);
        await page.ClickAsync("#web-serial");
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("try the other button");
        await Assertions.Expect(page.Locator("#web-serial")).ToBeVisibleAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }
}
