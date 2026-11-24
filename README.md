# SpawnDev.Examples

Small, complete projects for the articles on [spawndev.com](https://www.spawndev.com/articles). Each folder is one example you can run on its own with the .NET 10 SDK.

| Example | What it shows | Live demo |
|---|---|---|
| [BrowserApis](BrowserApis) | Screen wake lock, page visibility and clipboard from C# with [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS) | [Open](https://lostbeard.github.io/SpawnDev.Examples/BrowserApis/) |

## Run an example

```bash
git clone https://github.com/LostBeard/SpawnDev.Examples
cd SpawnDev.Examples/BrowserApis
dotnet run
```

## Tests

`Examples.Tests` runs the published examples in Chromium. The articles take their code from these files.

```bash
dotnet publish BrowserApis -c Release -o _publish/BrowserApis
dotnet build Examples.Tests
pwsh Examples.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test Examples.Tests
```

## License

MIT
