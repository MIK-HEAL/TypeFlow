using System.IO;
using System.Text.Json;
using TypeFlow.Core;

namespace TypeFlow.Desktop;

public sealed record DesktopPreferences(bool Enabled = false, bool CompanionEnabled = true,
    bool CompanionLocked = false, string CompanionClick = "voice", double? CompanionLeft = null,
    double? CompanionTop = null, string CandidatePlacement = "below", double CandidateGap = 10,
    double CandidateScale = 1)
{
    public DesktopPreferences Normalize() => this with {
        CompanionClick = CompanionClick is "voice" or "script" or "language" or "settings" or "none" ? CompanionClick : "voice",
        CompanionLeft = CompanionLeft is { } x && double.IsFinite(x) ? x : null,
        CompanionTop = CompanionTop is { } y && double.IsFinite(y) ? y : null,
        CandidatePlacement = CandidatePlacement is "below" or "right" or "bottom" ? CandidatePlacement : "below",
        CandidateGap = double.IsFinite(CandidateGap) ? Math.Clamp(CandidateGap, 4, 40) : 10,
        CandidateScale = double.IsFinite(CandidateScale) ? Math.Clamp(CandidateScale, .5, 1.5) : 1
    };
}

internal sealed class DesktopPreferenceStore(string file)
{
    public DesktopPreferences Read()
    {
        try { return (JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(file), ThemeFiles.Json) ?? new()).Normalize(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }

    public void Update(Func<DesktopPreferences, DesktopPreferences> update)
    {
        var value = update(Read()).Normalize();
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, ThemeFiles.Json));
        File.Move(temporary, file, true);
    }
}
