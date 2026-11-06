using TypeFlow.Core;

namespace TypeFlow.Rendering.Wpf;

// Keep image decoding and renderer capability checks outside the shared theme service.
public sealed class WpfThemeValidator : IThemeValidator
{
    public void Validate(Theme theme, string directory) => new CandidateSurface().SetTheme(theme, directory);
}

