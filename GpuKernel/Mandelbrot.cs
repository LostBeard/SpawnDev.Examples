using ILGPU;
using ILGPU.Runtime;

namespace GpuKernel;

public static class Mandelbrot
{
    /// <summary>The kernel: the GPU runs this once for every pixel.</summary>
    public static void Kernel(Index2D pixel, ArrayView2D<uint, Stride2D.DenseX> image, int maxSteps) =>
        image[pixel] = Color(pixel.X, pixel.Y, (int)image.Extent.X, (int)image.Extent.Y, maxSteps);

    /// <summary>Plain C#: the color of one pixel.</summary>
    public static uint Color(int x, int y, int width, int height, int maxSteps)
    {
        float cx = x * 3.0f / width - 2.2f;
        float cy = y * 2.4f / height - 1.2f;
        float zx = 0, zy = 0;
        int step = 0;
        while (step < maxSteps && zx * zx + zy * zy < 4)
        {
            float next = zx * zx - zy * zy + cx;
            zy = 2 * zx * zy + cy;
            zx = next;
            step++;
        }
        if (step == maxSteps) return 0xFF000000; // inside the set: black
        uint shade = (uint)(step * 8 & 255);
        return 0xFF000000 | shade << 16 | (shade / 2) << 8 | 40;
    }
}
