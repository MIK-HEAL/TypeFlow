using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Wpf;

public sealed record CompanionFrame(BitmapSource Image, int DelayMs);

public static class CompanionAnimation
{
    public static IReadOnlyList<CompanionFrame> Load(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > GifAnimationInfo.MaxBytes) throw new InvalidDataException("挂件 GIF 超过 4 MiB。");
        var data = new byte[(int)file.Length];
        file.ReadExactly(data);
        var info = GifAnimationInfo.Read(data);
        using var stream = new MemoryStream(data, writable: false);
        var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        if (decoder.Frames.Count != info.Frames.Count) throw new InvalidDataException("挂件 GIF 帧数不一致。");

        var result = new List<CompanionFrame>(info.Frames.Count);
        var canvas = new byte[info.Width * info.Height * 4];
        for (var i = 0; i < info.Frames.Count; i++)
        {
            var frame = info.Frames[i];
            BitmapSource source = decoder.Frames[i];
            if ((source.PixelWidth != frame.Width || source.PixelHeight != frame.Height) &&
                source.PixelWidth == info.Width && source.PixelHeight == info.Height)
                source = new CroppedBitmap(source, new Int32Rect(frame.X, frame.Y, frame.Width, frame.Height));
            if (source.PixelWidth != frame.Width || source.PixelHeight != frame.Height)
                throw new InvalidDataException("挂件 GIF 帧尺寸不一致。");
            if (source.Format != PixelFormats.Bgra32) source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var pixels = new byte[frame.Width * frame.Height * 4];
            source.CopyPixels(pixels, frame.Width * 4, 0);
            var previous = frame.Disposal == 3 ? (byte[])canvas.Clone() : null;
            for (var row = 0; row < frame.Height; row++)
            {
                var from = row * frame.Width * 4;
                var to = ((frame.Y + row) * info.Width + frame.X) * 4;
                for (var x = 0; x < frame.Width; x++, from += 4, to += 4)
                    if (pixels[from + 3] != 0) Array.Copy(pixels, from, canvas, to, 4);
            }
            var image = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Bgra32, null, canvas, info.Width * 4);
            image.Freeze();
            result.Add(new(image, frame.DelayMs));

            if (previous != null) canvas = previous;
            else if (frame.Disposal == 2)
                for (var row = 0; row < frame.Height; row++)
                    Array.Clear(canvas, ((frame.Y + row) * info.Width + frame.X) * 4, frame.Width * 4);
        }
        return result;
    }
}
