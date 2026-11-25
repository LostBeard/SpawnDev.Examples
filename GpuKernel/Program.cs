using GpuKernel;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.SpawnJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddSpawnJSRuntime();

await builder.Build().SpawnJSRunAsync();
