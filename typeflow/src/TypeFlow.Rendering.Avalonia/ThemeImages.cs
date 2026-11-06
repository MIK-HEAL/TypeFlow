using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Avalonia;

// The first backend accepts bounded static PNGs; it never silently flattens animations.
public static class ThemeImages
{
    public static Bitmap Load(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("图片超过 16 MiB。");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
            !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new NotSupportedException("跨平台小样当前只支持静态 PNG 图片。");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is 0 or > 4096 || height is 0 or > 4096 || (long)width * height > 8_000_000)
            throw new InvalidDataException("图片尺寸超过 4096×4096 或 800 万像素。");
        var ended = false;
        for (var offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12) throw new InvalidDataException("PNG 数据不完整。");
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("PNG 数据块越界。");
            var type = bytes.AsSpan(offset + 4, 4);
            if (type.SequenceEqual("acTL"u8)) throw new NotSupportedException("跨平台小样暂不支持动画 PNG。");
            offset += (int)length + 12;
            if (type.SequenceEqual("IEND"u8)) { ended = true; break; }
        }
        if (!ended) throw new InvalidDataException("PNG 缺少结束标记。");
        using var stream = new MemoryStream(bytes, false);
        using var bitmap = new Bitmap(stream);
        if (bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
            throw new InvalidDataException("PNG 解码尺寸不一致。");
        // Theme pixels map to 96-DPI DIP regardless of PNG metadata or decoder backend.
        var normalized = new WriteableBitmap(bitmap.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        try
        {
            using var frame = normalized.Lock();
            bitmap.CopyPixels(frame);
            return normalized;
        }
        catch { normalized.Dispose(); throw; }
    }

    public static void ValidateSlice(Bitmap image, Insets? slice)
    {
        var s = slice ?? new(0, 0, 0, 0);
        if (s.Left + s.Right >= image.PixelSize.Width || s.Top + s.Bottom >= image.PixelSize.Height)
            throw new InvalidDataException("九宫格边界必须在源图范围内。");
    }
}

public sealed class AvaloniaThemeValidator : IThemeValidator
{
    public void Validate(Theme theme, string directory)
    {
        using var surface = new CandidateSurface();
        surface.SetTheme(theme, directory);
    }
}

public sealed class AvaloniaThemeImageTranscoder : IThemeImageTranscoder
{
    public void WriteStaticPng(string source, string destination)
    {
        using var image = ThemeImages.Load(source);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        image.Save(output, new PngBitmapEncoderOptions());
    }
}
