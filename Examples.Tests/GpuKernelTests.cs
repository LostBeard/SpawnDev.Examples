using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED GpuKernel example in a real Chromium on whichever backend the library picks there.</summary>
[TestClass]
public sealed class GpuKernelTests
{
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _app = await PublishedExample.StartAsync("GpuKernel");
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

    const string ReadCanvas = """
        () => {
            const c = document.getElementById('canvas');
            return Array.from(new Uint32Array(c.getContext('2d').getImageData(0, 0, c.width, c.height).data.buffer));
        }
        """;

    static async Task<uint[]> Draw(string button, string resultStart)
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error" && !m.Text.Contains("404")) errors.Add(m.Text); };
        await page.GotoAsync(_app.Urls.First());
        await page.ClickAsync(button);
        var result = page.Locator("#result");
        await Assertions.Expect(result).ToHaveTextAsync(new Regex(" ms$"), new() { Timeout = 60000 });
        var text = await result.InnerTextAsync();
        Console.WriteLine($"{button}: {text}");
        StringAssert.StartsWith(text, resultStart);
        var pixels = await page.EvaluateAsync<double[]>(ReadCanvas);
        await page.CloseAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
        return pixels.Select(p => (uint)p).ToArray();
    }

    [TestMethod]
    public async Task PageAndGpu_DrawTheSamePicture()
    {
        var page = await Draw("#cpu", "Page:");
        var gpu = await Draw("#gpu", "GPU (");
        Assert.HasCount(page.Length, gpu);
        Assert.HasCount(33, page.Distinct().ToArray(), "32 shades plus black: a blank or broken picture has fewer");
        var same = page.Zip(gpu).Count(p => p.First == p.Second) * 100.0 / page.Length;
        Console.WriteLine($"matching pixels: {same:F2}%");
        // GPUs may round differently on the fractal's edge, where one step changes the color.
        Assert.IsGreaterThan(99, same); // a wrong step limit (20) still matches 96.8%
    }
}
