using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.WebWorkers;
using WebWorkers;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSpawnJSRuntime(out var JS);
builder.Services.AddWebWorkerService();

// A worker runs this file too, but it has no page to render into.
if (JS.IsWindow)
{
    builder.RootComponents.Add<App>("#app");
}

await builder.Build().SpawnJSRunAsync();
