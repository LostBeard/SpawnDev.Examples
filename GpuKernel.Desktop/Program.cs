using System.Diagnostics;
using GpuKernel;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

const int Width = 1000, Height = 800, MaxSteps = 500;

using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();
using var image = accelerator.Allocate2DDenseX<uint>(new Index2D(Width, Height));
var kernel = accelerator.LoadAutoGroupedStreamKernel<Index2D, ArrayView2D<uint, Stride2D.DenseX>, int>(Mandelbrot.Kernel);

// The first run also compiles the kernel for this GPU, so do it once before timing.
kernel(image.IntExtent, image.View, MaxSteps);
await accelerator.SynchronizeAsync();

var timer = Stopwatch.StartNew();
kernel(image.IntExtent, image.View, MaxSteps);
await accelerator.SynchronizeAsync();
timer.Stop();
Console.WriteLine($"{accelerator.AcceleratorType} ({accelerator.Name}): {timer.Elapsed.TotalMilliseconds:F1} ms");

// Same method, plain C#, one thread.
timer.Restart();
var expected = new uint[Width * Height];
for (var y = 0; y < Height; y++)
    for (var x = 0; x < Width; x++)
        expected[y * Width + x] = Mandelbrot.Color(x, y, Width, Height, MaxSteps);
timer.Stop();
Console.WriteLine($"One CPU thread: {timer.Elapsed.TotalMilliseconds:F1} ms");

var pixels = image.GetAsArray2D();
var same = 0;
for (var y = 0; y < Height; y++)
    for (var x = 0; x < Width; x++)
        if (pixels[x, y] == expected[y * Width + x]) same++;
Console.WriteLine($"Matching pixels: {100.0 * same / expected.Length:F2}%");
