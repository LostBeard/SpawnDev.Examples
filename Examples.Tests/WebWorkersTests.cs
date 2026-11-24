using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED WebWorkers example in a real Chromium.</summary>
[TestClass]
public sealed class WebWorkersTests
{
    const string Expected = "Found 412,849 primes."; // pi(6,000,000), from an independent sieve
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _app = await PublishedExample.StartAsync("WebWorkers");
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

    /// <summary>Clicks a button, waits for the answer, and returns the longest gap between updates of the page's clock.</summary>
    static async Task<(int LongestGapMs, int Workers)> Count(string button)
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await page.GotoAsync(_app.Urls.First());
        await Assertions.Expect(page.Locator("#clock")).Not.ToBeEmptyAsync();
        await page.EvaluateAsync("""
            () => {
                window.longestGap = 0;
                let last = performance.now();
                new MutationObserver(() => {
                    const now = performance.now();
                    window.longestGap = Math.max(window.longestGap, now - last);
                    last = now;
                }).observe(document.getElementById('clock'), { characterData: true, childList: true, subtree: true });
            }
            """);
        await page.ClickAsync(button);
        await Assertions.Expect(page.Locator("#result")).ToContainTextAsync(Expected, new() { Timeout = 60000 });
        await page.WaitForTimeoutAsync(500); // let the clock report a gap that has just ended
        var gap = await page.EvaluateAsync<double>("() => longestGap");
        var workers = page.Workers.Count;
        // The page tells the visitor the same thing this test measures from outside.
        var shown = await page.InnerTextAsync("#result");
        var shownMs = 1000 * double.Parse(Regex.Match(shown, @"was ([0-9.]+) seconds").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.IsTrue(Math.Abs(shownMs - gap) < Math.Max(100, gap * 0.1), $"page says {shownMs} ms, measured {gap:F0} ms");
        await page.CloseAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
        Console.WriteLine($"{button}: longest clock stop {gap:F0} ms, {workers} worker(s)");
        return ((int)gap, workers);
    }

    [TestMethod]
    public async Task OnThePage_TheUiFreezes_InAWorker_ItDoesNot()
    {
        var here = await Count("#here");
        var worker = await Count("#worker");
        Assert.AreEqual(0, here.Workers);
        Assert.IsGreaterThan(0, worker.Workers);
        // The comparison, not an absolute time, so a slow machine does not change the result.
        Assert.IsLessThan(here.LongestGapMs / 2, worker.LongestGapMs, $"page {here.LongestGapMs} ms, worker {worker.LongestGapMs} ms");
    }
}
