# SpawnDev.Examples

Small, complete projects for the articles on [spawndev.com](https://www.spawndev.com/articles). Each folder is one example you can run on its own with the .NET 10 SDK.

| Example | What it shows | Live demo |
|---|---|---|
| [BrowserApis](BrowserApis) | Screen wake lock, page visibility and clipboard from C# with [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS) | [Open](https://lostbeard.github.io/SpawnDev.Examples/BrowserApis/) |
| [WebWorkers](WebWorkers) | Slow C# in a Web Worker with [SpawnDev.SpawnJS.WebWorkers](https://github.com/LostBeard/SpawnDev.SpawnJS.WebWorkers) | [Open](https://lostbeard.github.io/SpawnDev.Examples/WebWorkers/) |
| [LargeFiles](LargeFiles) | Stream a large file to browser storage without .NET memory, with [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS) | [Open](https://lostbeard.github.io/SpawnDev.Examples/LargeFiles/) |
| [GpuKernel](GpuKernel) | One C# GPU kernel in Blazor and on the [desktop](GpuKernel.Desktop), with [SpawnDev.ILGPU](https://github.com/LostBeard/SpawnDev.ILGPU) | [Open](https://lostbeard.github.io/SpawnDev.Examples/GpuKernel/) |
| [ImageAI](ImageAI) | Name what is in a photo with a neural network on the visitor's own device, with [SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML) | [Open](https://lostbeard.github.io/SpawnDev.Examples/ImageAI/) |

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
dotnet publish WebWorkers -c Release -o _publish/WebWorkers
dotnet publish LargeFiles -c Release -o _publish/LargeFiles
dotnet publish GpuKernel -c Release -o _publish/GpuKernel
dotnet publish ImageAI -c Release -o _publish/ImageAI
dotnet build Examples.Tests
pwsh Examples.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test Examples.Tests
```

## License

MIT, except:

- `LargeFiles/wwwroot/BigBuckBunny-30.mp4`: Big Buck Bunny (c) 2008 Blender Foundation, [peach.blender.org](https://peach.blender.org), Sunflower version by Janus Bager Kristensen (2013), 30 second clip, [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/).
- `ImageAI/wwwroot/mobilenetv2-7.onnx`: MobileNetV2 from the [ONNX Model Zoo](https://huggingface.co/onnxmodelzoo/mobilenetv2-7), Apache-2.0.
- `ImageAI/wwwroot/red-panda.jpg` ([source](https://commons.wikimedia.org/wiki/File:Red_Panda_(24986761703).jpg)) and `labrador.jpg` ([source](https://commons.wikimedia.org/wiki/File:Labrador_Retriever_(1210559).jpg)): CC0.
