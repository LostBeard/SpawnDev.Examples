using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED BrowserApis example in a real Chromium against the real browser APIs.</summary>
[TestClass]
public sealed class BrowserApisTests
{
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;
    static string _url = "";

    // Records the real sentinels the browser hands out; the API itself is untouched.
    const string RecordSentinels = """
        window.sentinels = [];
        const request = WakeLock.prototype.request;
        WakeLock.prototype.request = async function (type) {
            const s = await request.call(this, type);
            window.sentinels.push(s);
            return s;
        };
        """;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        // The same tests run the SpawnJS and the BlazorJS versions of the page.
        _app = await PublishedExample.StartAsync(Environment.GetEnvironmentVariable("BROWSERAPIS_APP") ?? "BrowserApis");
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

    static async Task<(IPage Page, List<string> Errors)> Open(params string[] permissions)
    {
        var context = await _browser.NewContextAsync(new() { Permissions = permissions });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await page.AddInitScriptAsync(RecordSentinels);
        await page.GotoAsync(_url);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Pancakes");
        return (page, errors);
    }

    static Task<bool[]> Released(IPage page) => page.EvaluateAsync<bool[]>("() => sentinels.map(s => s.released)");

    [TestMethod]
    public async Task CookMode_HoldsAndReleasesTheScreenLock()
    {
        var (page, errors) = await Open("screen-wake-lock");
        var screen = page.Locator("#screen");
        await page.ClickAsync("#cook");
        await Assertions.Expect(screen).ToContainTextAsync("staying on");
        CollectionAssert.AreEqual(new[] { false }, await Released(page));
        await page.ClickAsync("#cook");
        await Assertions.Expect(screen).ToContainTextAsync("may sleep");
        CollectionAssert.AreEqual(new[] { true }, await Released(page));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task CookMode_TakesTheLockBack_AfterTheBrowserReleasesIt()
    {
        var (page, errors) = await Open("screen-wake-lock");
        var screen = page.Locator("#screen");
        await page.ClickAsync("#cook");
        await Assertions.Expect(screen).ToContainTextAsync("staying on");
        // A headless tab is never hidden, so release the real sentinel as the browser would.
        await page.EvaluateAsync("() => sentinels[0].release()");
        await Assertions.Expect(screen).ToContainTextAsync("may sleep");
        await page.EvaluateAsync("() => document.dispatchEvent(new Event('visibilitychange'))");
        await Assertions.Expect(screen).ToContainTextAsync("staying on");
        CollectionAssert.AreEqual(new[] { true, false }, await Released(page));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task CookMode_ReportsARefusal()
    {
        var (page, errors) = await Open(); // headless Chromium denies wake locks unless granted
        await page.ClickAsync("#cook");
        await Assertions.Expect(page.Locator("#status")).ToContainTextAsync("The browser refused");
        await Assertions.Expect(page.Locator("#cook")).ToHaveTextAsync("Start cook mode");
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task Copy_PutsTheIngredientsOnTheClipboard()
    {
        var (page, errors) = await Open("clipboard-read", "clipboard-write");
        await page.ClickAsync("#copy");
        await Assertions.Expect(page.Locator("#status")).ToHaveTextAsync("Copied.");
        var text = await page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        Assert.AreEqual("2 cups flour\n2 eggs\n1 1/2 cups milk\n1 tbsp sugar", text.Replace("\r\n", "\n"));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }
}
