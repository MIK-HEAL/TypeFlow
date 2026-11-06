using TypeFlow.Core;
using TypeFlow.Importers.Ssf;
using TypeFlow.Rendering.Wpf;
using TypeFlow.Themes;

namespace TypeFlow.Desktop;

// The executable chooses implementations; shared components never discover executable plugins.
public static class DesktopComponents
{
    public static ThemeLibrary CreateThemeLibrary(string root)
    {
        var importers = new ImporterRegistry();
        importers.Register(new SsfImporter());
        importers.Register(new NativeThemeImporter());
        return new(root, importers, new WpfThemeValidator());
    }
}

