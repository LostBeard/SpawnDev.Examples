using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace Examples.Tests;

/// <summary>Serves a published example (_publish/{name}/wwwroot) on a free local port.</summary>
static class PublishedExample
{
    public static async Task<WebApplication> StartAsync(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "BrowserApis"))) dir = dir.Parent;
        var root = Path.Combine(dir!.FullName, "_publish", name, "wwwroot");
        if (!File.Exists(Path.Combine(root, "index.html"))) throw new InvalidOperationException($"Publish {name} first: {root}");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        var files = new PhysicalFileProvider(root);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".dat"] = "application/octet-stream";
        types.Mappings[".onnx"] = "application/octet-stream";
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types });
        await app.StartAsync();
        return app;
    }
}
