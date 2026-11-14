using System.Text.Json;
using System.IO;
using System.Windows;
using TypeFlow.Core;
using TypeFlow.Desktop;
using TypeFlow.Engine.Rime;

internal static class DesktopInteractionTests
{
    // Isolated manual UI fixture: no installed preferences or IME registration changes.
    public static int Preview(string repo)
    {
        var root = Path.Combine(repo, "typeflow/build/interaction-preview");
        var library = DesktopComponents.CreateThemeLibrary(root);
        if (!library.List().Any(x => x.Theme.Companion != null))
            library.Activate(library.Import(Path.Combine(repo, "test/ssf/尼古喵喵！.ssf")).GetAwaiter().GetResult().ThemeDirectory);
        else library.Activate(library.List().First(x => x.Theme.Companion != null).Directory);
        var store = new DesktopPreferenceStore(Path.Combine(root, "preferences.json"));
        var app = new Application();
        MainWindow? main = null;
        using var companion = new CompanionController(library, store.Read, store.Update,
            action => CompanionActions.Execute(action, () => main?.Activate()));
        main = new MainWindow(library, Path.Combine(repo, "typeflow"),
            value => store.Update(p => p with { Enabled = value }), () => store.Read().Enabled,
            value => { store.Update(p => p with { CompanionEnabled = value }); companion.Refresh(); },
            () => store.Read().CompanionEnabled, store.Read, store.Update, companion.ResetPosition);
        app.Startup += (_, _) => companion.Refresh();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return app.Run(main);
    }

    public static int Run(RimeEngine engine)
    {
        var passed = 0;
        void Check(bool result, string label) { if (!result) throw new Exception("FAIL: " + label); passed++; }
        var work = new Rect(0, 0, 1920, 1040);
        var size = new Size(500, 160);
        var caret = new Rect(400, 300, 2, 24);
        var p = CandidatePlacement.Locate(caret, work, size, 10, "below");
        Check(p == new Point(400, 334), "candidate below text with gap");
        caret = new Rect(400, 960, 2, 24);
        p = CandidatePlacement.Locate(caret, work, size, 10, "below");
        Check(p.X >= caret.Right + 10 && !new Rect(p, size).IntersectsWith(caret), "tall skin moves sideways before above");
        caret = new Rect(1880, 960, 2, 24);
        p = CandidatePlacement.Locate(caret, work, size, 10, "below");
        Check(p.X + size.Width <= caret.Left - 10 && work.Contains(new Rect(p, size)), "right edge uses left side");
        var second = new Rect(-1920, -200, 1920, 1040);
        p = CandidatePlacement.Locate(new Rect(-500, 100, 2, 30), second, new Size(750, 240), 15, "below");
        Check(second.Contains(new Rect(p, new Size(750, 240))) && p.Y == 145, "negative monitor coordinates and scaled spacing");
        p = CandidatePlacement.Locate(caret, work, size, 10, "bottom");
        Check(p == new Point(710, 870), "fixed bottom placement");
        p = CandidatePlacement.Locate(new Rect(300, 200, 2, 24), work, size, 16, "right");
        Check(p == new Point(318, 200), "right placement preference");
        p = CandidatePlacement.Locate(new Rect(900, 900, 2, 24), work, new Size(1800, 300), 10, "below");
        Check(p.Y == 590, "wide skin safely falls back above if neither side fits");
        var old = JsonSerializer.Deserialize<DesktopPreferences>("{\"enabled\":true,\"companionEnabled\":false}", ThemeFiles.Json)!;
        Check(old.Enabled && !old.CompanionEnabled && old.CandidateGap == 10 && old.CompanionClick == "voice", "old preferences preserve enabled state and gain defaults");
        var invalid = (old with { CandidateScale = double.NaN, CandidateGap = -1, CandidatePlacement = "bad" }).Normalize();
        Check(invalid.CandidateScale == 1 && invalid.CandidateGap == 4 && invalid.CandidatePlacement == "below", "invalid placement preferences are bounded");
        engine.Clear();
        foreach (var k in "hanyu") engine.ProcessKey(k);
        var simplified = engine.View.Candidates[0].Text;
        engine.ProcessKey('4', 5); // Rime ShiftMask | ControlMask
        engine.ProcessKey('4', 5 | (1 << 30));
        var traditional = engine.View.Candidates[0].Text;
        Check(simplified == "汉语" && traditional == "漢語", "companion shortcut switches real composing candidates to traditional");
        engine.ProcessKey('4', 5);
        engine.ProcessKey('4', 5 | (1 << 30));
        Check(engine.View.Candidates[0].Text == "汉语", "companion shortcut restores simplified output: " + engine.View.Candidates[0].Text);
        engine.ProcessKey('$', 5);
        engine.ProcessKey('$', 5 | (1 << 30));
        Check(engine.View.Candidates[0].Text == "漢語", "shifted Windows dollar key also switches to traditional");
        engine.ProcessKey('$', 5);
        engine.ProcessKey('$', 5 | (1 << 30));
        Check(engine.View.Candidates[0].Text == "汉语", "shifted Windows dollar key restores simplified");
        engine.Clear();
        engine.ProcessKey('2', 5);
        engine.ProcessKey('2', 5 | (1 << 30));
        Check(!engine.ProcessKey('n').Handled, "language shortcut enters ASCII mode");
        engine.ProcessKey('2', 5);
        engine.ProcessKey('2', 5 | (1 << 30));
        Check(engine.ProcessKey('n').Handled && engine.View.Preedit == "n", "language shortcut returns to Chinese mode");
        engine.Clear();
        return passed;
    }
}
