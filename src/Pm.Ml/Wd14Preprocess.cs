using Microsoft.ML.OnnxRuntime.Tensors;
using Pm.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Pm.Ml;

public static class Wd14Preprocess
{
    // WD14:方形白底 padding → resize size² → BGR、0–255 float、NHWC [1,size,size,3]。
    public static DenseTensor<float> ToTensor(string absPath, int size = 448)
    {
        using var img = ImageLoader.LoadRgb24(absPath);   // 引擎選擇由 Pm.Imaging 內政決定

        var side = Math.Max(img.Width, img.Height);
        using var canvas = new Image<Rgb24>(side, side, new Rgb24(255, 255, 255));
        var ox = (side - img.Width) / 2;
        var oy = (side - img.Height) / 2;

        // 置中貼到白底方形畫布(等價於方形 padding)。
        canvas.Mutate(c => c.DrawImage(img, new Point(ox, oy), 1f));
        canvas.Mutate(c => c.Resize(size, size));

        var tensor = new DenseTensor<float>(new[] { 1, size, size, 3 });
        canvas.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                {
                    var p = row[x];
                    tensor[0, y, x, 0] = p.B;   // BGR
                    tensor[0, y, x, 1] = p.G;
                    tensor[0, y, x, 2] = p.R;
                }
            }
        });
        return tensor;
    }
}
