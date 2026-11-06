using System.IO;
using System.Windows.Media.Imaging;
using TypeFlow.Core;

namespace TypeFlow.Rendering.Wpf;

public sealed class WpfThemeImageTranscoder : IThemeImageTranscoder
{
    public void WriteStaticPng(string source, string destination)
    {
        var image = ThemeImages.Load(source);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        encoder.Save(output);
    }
}
