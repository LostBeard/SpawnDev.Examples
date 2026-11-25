using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Examples.Tests;

/// <summary>Runs the PUBLISHED ImageAI example (real model, real photos) in a real Chromium on whichever backend the library picks there.</summary>
[TestClass]
public sealed class ImageAITests
{
    static WebApplication _app = null!;
    static IPlaywright _playwright = null!;
    static IBrowser _browser = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _app = await PublishedExample.StartAsync("ImageAI");
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

    static async Task<string[]> Answers(IPage page)
    {
        await Assertions.Expect(page.Locator("#status")).ToHaveTextAsync(new Regex("^Done|did not work"), new() { Timeout = 90000 });
        Console.WriteLine(await page.InnerTextAsync("#status"));
        var answers = (await page.Locator("#answers li").AllInnerTextsAsync()).ToArray();
        Console.WriteLine(string.Join(" | ", answers));
        return answers;
    }

    [TestMethod]
    public async Task Photos_AreRecognized_AndNothingLeavesTheDevice()
    {
        var page = await _browser.NewPageAsync();
        var errors = new List<string>();
        var elsewhere = new List<string>();
        page.PageError += (_, e) => errors.Add(e);
        page.Console += (_, m) => { if (m.Type == "error" && !m.Text.Contains("404")) errors.Add(m.Text); };
        page.Request += (_, r) =>
        {
            var local = r.Url.StartsWith(_app.Urls.First()) || r.Url.StartsWith("blob:") || r.Url.StartsWith("data:");
            if (!local || r.Method != "GET") elsewhere.Add($"{r.Method} {r.Url}");
        };
        await page.GotoAsync(_app.Urls.First());

        await page.ClickAsync("#panda");
        var answers = await Answers(page);
        Assert.HasCount(3, answers);
        StringAssert.Contains(answers[0], "lesser panda", "red panda sample"); // ImageNet's name for a red panda

        await page.ClickAsync("#dog");
        await Assertions.Expect(page.Locator("#answers li").First).Not.ToHaveTextAsync(answers[0], new() { Timeout = 60000 });
        answers = await Answers(page);
        StringAssert.Contains(answers[0], "Labrador retriever", "labrador sample");

        // The visitor's own photo goes through the same path.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ImageAI"))) dir = dir.Parent;
        await page.Locator("#pick").SetInputFilesAsync(Path.Combine(dir!.FullName, "ImageAI", "wwwroot", "red-panda.jpg"));
        await Assertions.Expect(page.Locator("#answers li").First).ToContainTextAsync("lesser panda", new() { Timeout = 60000 });

        Assert.IsEmpty(elsewhere, "requests that left the page's own server: " + string.Join(", ", elsewhere));
        Assert.IsEmpty(errors, string.Join(" | ", errors));
        await page.CloseAsync();
    }
}
