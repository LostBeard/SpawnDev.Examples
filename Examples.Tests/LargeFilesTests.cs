using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED LargeFiles example in a real Chromium, against the real origin private file system.</summary>
[TestClass]
public sealed class LargeFilesTests
{
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _app = await PublishedExample.StartAsync("LargeFiles");
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

    static async Task<double> Save(string button, byte[]? ownFile = null)
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        // Reads the file back from storage just before the example deletes it.
        await page.AddInitScriptAsync("""
            const remove = FileSystemDirectoryHandle.prototype.removeEntry;
            FileSystemDirectoryHandle.prototype.removeEntry = async function (name) {
                const file = await (await this.getFileHandle(name)).getFile();
                const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer()));
                window.savedHash = [...hash].map(b => b.toString(16).padStart(2, '0')).join('');
                return remove.call(this, name);
            };
            """);
        await page.GotoAsync(_app.Urls.First());
        var content = ownFile ?? new byte[200 * 1048576];
        if (ownFile is not null) await page.Locator("#pick").SetInputFilesAsync(new FilePayload { Name = "own.bin", MimeType = "application/octet-stream", Buffer = ownFile });
        await page.ClickAsync(button);
        var result = page.Locator("#result");
        await Assertions.Expect(result).ToHaveTextAsync(new Regex("Saved|Could not"), new() { Timeout = 60000 });
        var text = await result.InnerTextAsync();
        Console.WriteLine($"{button}: {text}");
        StringAssert.Contains(text, $"Saved {(content.Length / 1048576.0).ToString("F1", CultureInfo.InvariantCulture)} MB.");
        await Assertions.Expect(page.Locator(button)).ToBeEnabledAsync();
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(content)), await page.EvaluateAsync<string>("() => savedHash"), "saved bytes");
        var left = await page.EvaluateAsync<int>("async () => { let n = 0; for await (const _ of (await navigator.storage.getDirectory()).keys()) n++; return n; }");
        Assert.AreEqual(0, left, "the test file must be deleted");
        await page.CloseAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
        return double.Parse(Regex.Match(text, "grew by ([0-9.]+) MB").Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [TestMethod]
    public async Task ThroughDotNet_TakesTheWholeFile() => Assert.IsGreaterThan(190, await Save("#copy"));

    [TestMethod]
    public async Task Streamed_TakesAlmostNothing() => Assert.IsLessThan(5, await Save("#stream"));

    [TestMethod]
    public async Task OwnFile_IsSavedUnchanged()
    {
        var bytes = new byte[8 * 1048576 + 123];
        new Random(1).NextBytes(bytes);
        Assert.IsLessThan(5, await Save("#stream", bytes));
        Assert.IsGreaterThan(7, await Save("#copy", bytes));
    }
}
