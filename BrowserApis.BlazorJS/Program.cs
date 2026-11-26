using BrowserApis.BlazorJS;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddBlazorJSRuntime();

await builder.Build().BlazorJSRunAsync();
