using System.Text;

namespace TypeFlow.Core;

public sealed record GifFrameInfo(int X, int Y, int Width, int Height, int DelayMs, int Disposal);

// Inspect the animation before a platform decoder allocates pixel buffers.
public sealed record GifAnimationInfo(int Width, int Height, IReadOnlyList<GifFrameInfo> Frames)
{
    public const int MaxBytes = 4 * 1024 * 1024;

    public static GifAnimationInfo Read(byte[] data)
    {
        if (data.Length > MaxBytes) throw new InvalidDataException("挂件 GIF 超过 4 MiB。");
        using var reader = new BinaryReader(new MemoryStream(data, writable: false));
        if (Encoding.ASCII.GetString(reader.ReadBytes(6)) is not ("GIF87a" or "GIF89a"))
            throw new InvalidDataException("挂件资源不是 GIF。");
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        if (width is 0 or > 1024 || height is 0 or > 1024)
            throw new InvalidDataException("挂件 GIF 尺寸超出 1024 × 1024。");

        var flags = reader.ReadByte();
        Skip(2); // background index and pixel aspect ratio
        SkipPalette(flags);
        var frames = new List<GifFrameInfo>();
        var delay = 100;
        var disposal = 0;
        while (true)
        {
            switch (reader.ReadByte())
            {
                case 0x3b when frames.Count > 0:
                    return new(width, height, frames);
                case 0x21:
                    var extension = reader.ReadByte();
                    if (extension == 0xf9)
                    {
                        if (reader.ReadByte() != 4) throw InvalidGif();
                        disposal = (reader.ReadByte() >> 2) & 7;
                        var centiseconds = reader.ReadUInt16();
                        delay = centiseconds < 2 ? 100 : centiseconds * 10;
                        Skip(1); // transparent color index
                        if (reader.ReadByte() != 0) throw InvalidGif();
                    }
                    else
                    {
                        SkipBlocks();
                        if (extension == 1) { delay = 100; disposal = 0; } // plain text consumes graphic control
                    }
                    break;
                case 0x2c:
                    var x = reader.ReadUInt16();
                    var y = reader.ReadUInt16();
                    var w = reader.ReadUInt16();
                    var h = reader.ReadUInt16();
                    if (w == 0 || h == 0 || x + w > width || y + h > height) throw InvalidGif();
                    frames.Add(new(x, y, w, h, delay, disposal));
                    if (frames.Count > 128 || (long)width * height * frames.Count > 12_000_000)
                        throw new InvalidDataException("挂件 GIF 帧数或解码尺寸超出限制。");
                    SkipPalette(reader.ReadByte());
                    Skip(1); // LZW code size
                    SkipBlocks();
                    delay = 100;
                    disposal = 0;
                    break;
                default:
                    throw InvalidGif();
            }
        }

        void Skip(int count)
        {
            if (count > reader.BaseStream.Length - reader.BaseStream.Position) throw InvalidGif();
            reader.BaseStream.Position += count;
        }
        void SkipPalette(byte packed)
        {
            if ((packed & 0x80) != 0) Skip(3 * (2 << (packed & 7)));
        }
        void SkipBlocks()
        {
            int length;
            while ((length = reader.ReadByte()) != 0) Skip(length);
        }
        static InvalidDataException InvalidGif() => new("挂件 GIF 结构无效。");
    }
}
