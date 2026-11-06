using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Wpf;

public static class ThemeImages
{
    public static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("图片超过 16 MiB。");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count != 1) throw new NotSupportedException("当前装饰图层只支持单帧静态图片。");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth < 1 || frame.PixelHeight < 1 || frame.PixelWidth > 4096 || frame.PixelHeight > 4096 || (long)frame.PixelWidth * frame.PixelHeight > 8_000_000)
            throw new InvalidDataException("图片尺寸超过 4096×4096 或 800 万像素。");
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight]; converted.CopyPixels(pixels, stride, 0);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
    public static void ValidateSlice(BitmapSource image, Insets? slice)
    {
        var s = slice ?? new(0, 0, 0, 0);
        if (s.Left + s.Right >= image.PixelWidth || s.Top + s.Bottom >= image.PixelHeight)
            throw new InvalidDataException("九宫格边界必须在源图范围内。");
    }
}

