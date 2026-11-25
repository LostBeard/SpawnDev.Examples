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
    public async Task Video_PlaysFromStorage_WithoutEnteringDotNet()
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await page.GotoAsync(_app.Urls.First());
        await page.ClickAsync("#play");
        await Assertions.Expect(page.Locator("#result")).ToHaveTextAsync("Choose a video file first.");
        // A real video, recorded by the browser from a canvas.
        var base64 = await page.EvaluateAsync<string>("""
            async () => {
                const canvas = Object.assign(document.createElement('canvas'), { width: 160, height: 90 });
                const ctx = canvas.getContext('2d');
                const recorder = new MediaRecorder(canvas.captureStream(30), { mimeType: 'video/webm' });
                const chunks = [];
                recorder.ondataavailable = e => chunks.push(e.data);
                const stopped = new Promise(r => recorder.onstop = r);
                recorder.start();
                for (let i = 0; i < 30; i++) {
                    ctx.fillStyle = `hsl(${i * 12} 100% 50%)`;
                    ctx.fillRect(0, 0, 160, 90);
                    await new Promise(r => setTimeout(r, 33));
                }
                recorder.stop();
                await stopped;
                const bytes = new Uint8Array(await new Blob(chunks).arrayBuffer());
                let s = '';
                for (const b of bytes) s += String.fromCharCode(b);
                return btoa(s);
            }
            """);
        await page.Locator("#pick").SetInputFilesAsync(new FilePayload { Name = "clip.webm", MimeType = "video/webm", Buffer = Convert.FromBase64String(base64) });
        await page.ClickAsync("#play");
        var result = page.Locator("#result");
        await Assertions.Expect(result).ToContainTextAsync("from storage");
        Console.WriteLine($"#play: {await result.InnerTextAsync()}");
        await page.WaitForFunctionAsync("() => { const v = document.getElementById('video'); return v && v.videoWidth === 160 && v.currentTime > 0.2; }");
        StringAssert.StartsWith(await page.Locator("#video").GetAttributeAsync("src"), "blob:");
        Assert.IsLessThan(5, double.Parse(Regex.Match(await result.InnerTextAsync(), "grew by ([0-9.]+) MB").Groups[1].Value, CultureInfo.InvariantCulture));
        await page.ClickAsync("#play"); // a second play replaces the first
        await page.WaitForFunctionAsync("() => { const v = document.getElementById('video'); return v && v.currentTime > 0.2 && !v.error; }");
        await page.CloseAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
    }

    [TestMethod]
    public async Task OwnFile_IsSavedUnchanged()
    {
        var bytes = new byte[8 * 1048576 + 123];
        new Random(1).NextBytes(bytes);
        Assert.IsLessThan(5, await Save("#stream", bytes));
        Assert.IsGreaterThan(7, await Save("#copy", bytes));
    }
}
