using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED WebWorkers example in a real Chromium.</summary>
[TestClass]
public sealed class WebWorkersTests
{
    const string Expected = "216816 primes"; // pi(3,000,000)
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

    /// <summary>Clicks a button, waits for the answer, and returns the longest gap between ticks of the page's heartbeat.</summary>
    static async Task<(int LongestGapMs, int Workers)> Count(string button)
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        await page.GotoAsync(_app.Urls.First());
        await Assertions.Expect(page.Locator("#ticks")).Not.ToHaveTextAsync("0");
        await page.EvaluateAsync("""
            () => {
                window.longestGap = 0;
                let last = performance.now();
                new MutationObserver(() => {
                    const now = performance.now();
                    window.longestGap = Math.max(window.longestGap, now - last);
                    last = now;
                }).observe(document.getElementById('ticks'), { characterData: true, childList: true, subtree: true });
            }
            """);
        await page.ClickAsync(button);
        await Assertions.Expect(page.Locator("#result")).ToHaveTextAsync(Expected, new() { Timeout = 60000 });
        await page.WaitForTimeoutAsync(500); // let the heartbeat report a gap that has just ended
        var gap = await page.EvaluateAsync<double>("() => longestGap");
        var workers = page.Workers.Count;
        await page.CloseAsync();
        Assert.IsEmpty(errors, string.Join(" | ", errors));
        Console.WriteLine($"{button}: longest heartbeat gap {gap:F0} ms, {workers} worker(s)");
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
