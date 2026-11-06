using System.Security.Cryptography;
using System.Text.Json;
using TypeFlow.Core;
using TypeFlow.Themes;

internal static class ThemeEditingTests
{
    public static int Run(string temp, string source)
    {
        var passed = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("EDIT FAIL: " + label); passed++; }
        void Reject<T>(Action action, string label) where T : Exception
        {
            try { action(); } catch (T) { passed++; return; }
            throw new Exception("EDIT FAIL: " + label);
        }
        var imports = new ImporterRegistry();
        imports.Register(new NativeThemeImporter());
        var validator = new StructuralValidator();
        var library = new ThemeLibrary(Path.Combine(temp, "editing-services"), imports, validator);
        var codec = new CopyingTestTranscoder();
        var sourceFile = Path.Combine(source, "theme.json");
        var originalHash = SHA256.HashData(File.ReadAllBytes(sourceFile));
        var original = ThemeFiles.Load(sourceFile);
        var image = ThemeFiles.AssetPath(source, ThemeFiles.ReferencedImages(original).First());
        string saved, draft;
        var edit = new ThemeEditSession(library, source, codec);
        using (edit)
        {
            draft = edit.Directory;
            Check(validator.Calls > 0 && !edit.IsDirty, "draft validates without a UI");
            var detached = edit.Current;
            detached.Extensions["test"] = JsonSerializer.SerializeToElement("mutated");
            Check(!edit.Current.Extensions.ContainsKey("test"), "view of current state is detached");
            edit.Update(t => t with { Layout = t.Layout with { PreeditPosition = new(170, 18), CandidatePosition = new(165, 48) } });
            edit.Move("preedit", 7, 5, new(170, 18));
            Check(edit.Current.Layout.PreeditPosition == new TextPosition(177, 23), "renderer supplies text origin in DIP");
            Check(edit.Current.Layout.CandidatePosition == new TextPosition(165, 48), "text positions are independent");
            edit.Undo();
            Check(edit.Current.Layout.PreeditPosition == new TextPosition(170, 18), "undo restores layout");
            edit.Redo();
            Check(edit.Current.Layout.PreeditPosition == new TextPosition(177, 23), "redo restores layout");
            var before = JsonSerializer.Serialize(edit.Current, ThemeFiles.Json);
            Reject<InvalidDataException>(() => edit.Update(t => t with { Text = t.Text with { FontSize = 999 } }), "invalid edits rejected");
            Check(JsonSerializer.Serialize(edit.Current, ThemeFiles.Json) == before, "failed edit preserves current and history");
            Reject<ArgumentException>(() => edit.Move("unknown", 1, 1, new(0, 0)), "unknown element rejected");
            Reject<ArgumentOutOfRangeException>(() => edit.Move("preedit", double.NaN, 1, new(0, 0)), "nonfinite movement rejected");
            var id = edit.AddLayer(image);
            Check(codec.Calls == 1 && File.Exists(ThemeFiles.AssetPath(draft, edit.Current.Layers[0].Image)), "image work goes through injected backend");
            edit.Move(id, 8, -4, new(0, 0));
            Check(edit.Current.Layers[0].X == 8 && edit.Current.Layers[0].Y == -4, "layers move without UI geometry");
            var filesBefore = Directory.GetFiles(Path.Combine(draft, "assets")).Length;
            codec.FailAfterWrite = true;
            Reject<IOException>(() => edit.CopyImage(image), "transcoder failure is reported");
            Check(Directory.GetFiles(Path.Combine(draft, "assets")).Length == filesBefore, "partial image output rolled back");
            codec.FailAfterWrite = false;
            saved = edit.SaveCopy("Portable edited skin");
            var model = ThemeFiles.Load(Path.Combine(saved, "theme.json"));
            Check(model.Id != original.Id && model.Author == original.Author && model.License == original.License, "new identity retains attribution");
            Check(File.ReadAllBytes(Path.Combine(saved, "import-report.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(source, "import-report.json"))), "conversion limitations retained");
            Check(SHA256.HashData(File.ReadAllBytes(sourceFile)).SequenceEqual(originalHash), "source remains intact");
            edit.Undo();
            edit.Update(t => t with { Name = "Branched edit" });
            Check(!edit.CanRedo, "new edit after undo truncates redo");
            edit.Reset();
            Check(!edit.IsDirty, "reset restores source model");
        }
        Check(!Directory.Exists(draft), "disposing cleans only the draft");
        var reloaded = ThemeFiles.Load(Path.Combine(saved, "theme.json"));
        Check(ThemeFiles.ReferencedImages(reloaded).All(x => File.Exists(ThemeFiles.AssetPath(saved, x))), "saved assets survive draft cleanup");
        Reject<ObjectDisposedException>(() => edit.Update(t => t), "disposed transaction rejects updates");
        return passed;
    }

    private sealed class StructuralValidator : IThemeValidator
    {
        public int Calls { get; private set; }
        public void Validate(Theme theme, string directory) { Calls++; ThemeFiles.Validate(theme, directory); }
    }

    // Real PNG normalization remains covered by WPF integration tests.
    // This injected backend verifies dispatch and rollback of incomplete writes.
    private sealed class CopyingTestTranscoder : IThemeImageTranscoder
    {
        public int Calls { get; private set; }
        public bool FailAfterWrite { get; set; }
        public void WriteStaticPng(string source, string destination)
        {
            Calls++;
            File.Copy(source, destination);
            if (FailAfterWrite) throw new IOException("Injected image backend failure.");
        }
    }
}
