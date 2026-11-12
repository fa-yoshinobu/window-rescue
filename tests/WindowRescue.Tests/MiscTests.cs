using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows.Input;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Settings;

namespace WindowRescue.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+1", ModifierKeys.Control | ModifierKeys.Alt, Key.D1)]
    [InlineData("Ctrl+Alt+Shift+3", ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift, Key.D3)]
    [InlineData("Ctrl+Alt+Space", ModifierKeys.Control | ModifierKeys.Alt, Key.Space)]
    [InlineData("Ctrl+Alt+Home", ModifierKeys.Control | ModifierKeys.Alt, Key.Home)]
    [InlineData("Ctrl+Alt+Backspace", ModifierKeys.Control | ModifierKeys.Alt, Key.Back)]
    public void ParsesDefaults(string text, ModifierKeys mods, Key key)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
        Assert.Equal(text, g.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+NoSuchKey")]
    public void RejectsInvalid(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void DefaultsCoverThreeDisplaysAndAreUnique()
    {
        var ids = HotkeyActions.All([1, 2]);
        Assert.Contains("RescueAll:3", ids);
        var gestures = ids.Select(id => HotkeyActions.Default(id)).Where(g => g.Length > 0).ToList();
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
        Assert.Equal(8, gestures.Count);
    }

    [Fact]
    public void FourthDisplayGetsItsOwnActions()
    {
        var ids = HotkeyActions.All([1, 2, 3, 4]);
        Assert.Contains("RescueForeground:4", ids);
        Assert.Equal("Ctrl+Alt+Shift+4", HotkeyActions.Default("RescueAll:4"));
    }

    [Fact]
    public void SparseConnectedNumbersIncludeBothActionsAndKeepFirstThree()
    {
        var ids = HotkeyActions.All([5, 4, 5, 10]);
        foreach (int number in new[] { 1, 2, 3, 4, 5, 10 })
        {
            Assert.Contains($"RescueForeground:{number}", ids);
            Assert.Contains($"RescueAll:{number}", ids);
        }
        Assert.Equal(14, ids.Count);
        Assert.DoesNotContain("RescueAll:6", ids);
        Assert.Equal("", HotkeyActions.Default("RescueForeground:10"));
        Assert.Equal("", HotkeyActions.Default("RescueAll:10"));
    }
}

public class CommandTests
{
    [Fact]
    public void RoundTrip()
    {
        var c = AppCommand.FromArgs(["--action", "rescue-here"], -1200, 300);
        Assert.Equal(c, AppCommand.Deserialize(c.Serialize()));
    }

    [Fact]
    public void UnknownActionIsIgnored()
    {
        Assert.Equal(AppCommand.None, AppCommand.FromArgs(["--action", "format-c"], 0, 0).Action);
        Assert.Null(AppCommand.Deserialize("format-c|0|0"));
    }

    [Fact]
    public void ContextMenuCommandQuotesPathWithSpacesAndJapanese()
    {
        var cmd = Core.ContextMenuRegistrar.CommandFor(@"C:\Program Files\窓 救出\WindowRescue.exe", AppCommand.RescueHere);
        Assert.Equal("\"C:\\Program Files\\窓 救出\\WindowRescue.exe\" --action rescue-here", cmd);
    }
}

public class DisplayManagerTests
{
    private static DisplayInfo D(string id, int x, bool primary = false) =>
        new(id, @"\\.\DISPLAY" + id, id, new PixelRect(x, 0, 1920, 1080), new PixelRect(x, 0, 1920, 1040), 96, primary);

    [Fact]
    public void NumbersLeftToRightAndDestinationUnderPointer()
    {
        var s = new AppSettings();
        var dm = new DisplayManager(s);
        dm.Refresh([D("b", 1920, true), D("a", -1920), D("c", 3840)], (2000, 10));
        Assert.Equal(1, dm.NumberOf(dm.ById("a")!));
        Assert.Equal(2, dm.NumberOf(dm.ById("b")!));
        Assert.Equal("b", dm.GetDestination((2000, 10))!.Id);
    }

    [Fact]
    public void ReorderedConnectionKeepsNumbers()
    {
        var s = new AppSettings();
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0, true), D("b", 1920), D("c", 3840)], (10, 10));
        dm.Refresh([D("c", 3840), D("b", 1920), D("a", 0, true)], (10, 10));
        Assert.Equal(3, dm.NumberOf(dm.ById("c")!));
    }

    [Fact]
    public void DisconnectedDestinationFallsBackToDisplayUnderPointer()
    {
        var s = new AppSettings();
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0, true), D("b", 1920), D("c", 3840)], (10, 10));
        dm.SetDestination("c");
        dm.Refresh([D("a", 0, true), D("b", 1920)], (2000, 10));
        Assert.Equal("b", dm.GetDestination((2000, 10))!.Id);
    }

    [Fact]
    public void CannotChooseDisconnectedDestination()
    {
        var s = new AppSettings();
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0, true)], (10, 10));
        Assert.False(dm.SetDestination("zzz"));
        Assert.Equal("a", dm.GetDestination((10, 10))!.Id);
    }

    [Fact]
    public void ExistingSettingsWithThreeDisconnectedDisplaysExposeConnectedFourAndFive()
    {
        var s = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""
            {"Version":1,"Displays":[{"Id":"old1","Number":1},{"Id":"old2","Number":2},{"Id":"old3","Number":3}]}
            """)!;
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0), D("b", 1920)], (10, 10));
        var ids = HotkeyActions.All(dm.Connected.Select(dm.NumberOf));
        Assert.Contains("RescueForeground:4", ids);
        Assert.Contains("RescueForeground:5", ids);
        Assert.Contains("RescueAll:4", ids);
        Assert.Contains("RescueAll:5", ids);
        Assert.Equal(5, s.Displays.Count);
    }

    [Fact]
    public void SwappingNumbersKeepsDestinationAndNumberBasedHotkeys()
    {
        var s = new AppSettings { Hotkeys = new() { ["RescueAll:1"] = "Ctrl+Shift+F1" } };
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0), D("b", 1920)], (10, 10));
        Assert.True(dm.SetNumber("a", 2));
        Assert.Equal("b", dm.ByNumber(1)!.Id);
        Assert.Equal("a", dm.ByNumber(2)!.Id);
        Assert.Equal("a", dm.GetDestination((10, 10))!.Id);
        Assert.Equal("Ctrl+Shift+F1", s.Hotkeys["RescueAll:1"]);
        Assert.True(dm.SetNumber("a", 10));
        Assert.Null(dm.ByNumber(2));
        Assert.Equal("a", dm.ByNumber(10)!.Id);
        Assert.False(dm.SetNumber("a", 0));
        Assert.False(dm.SetNumber("unknown", 1));
        Assert.Equal(10, dm.NumberOf(dm.ById("a")!));
    }

    [Fact]
    public void OnlyDisconnectedDisplayCanBeDeletedAndItsNumberIsReused()
    {
        var s = new AppSettings();
        var dm = new DisplayManager(s);
        dm.Refresh([D("a", 0), D("b", 1920)], (10, 10));
        Assert.False(dm.ForgetDisconnected("a"));
        dm.Refresh([D("b", 1920)], (2000, 10));
        Assert.True(dm.ForgetDisconnected("a"));
        dm.Refresh([D("b", 1920), D("c", 3840)], (2000, 10));
        Assert.Equal("c", dm.ByNumber(1)!.Id);
        Assert.Equal("b", dm.ByNumber(2)!.Id);
    }
}

public class LocalizationTests
{
    private static Dictionary<string, string> Read(CultureInfo c)
    {
        var rm = new ResourceManager("WindowRescue.Resources.Strings", typeof(Loc).Assembly);
        var set = rm.GetResourceSet(c, true, false)!;
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Fact]
    public void JapaneseAndEnglishHaveSameKeysAndPlaceholders()
    {
        var en = Read(CultureInfo.InvariantCulture);
        var ja = Read(CultureInfo.GetCultureInfo("ja"));
        Assert.Equal(en.Keys.OrderBy(k => k), ja.Keys.OrderBy(k => k));
        var ph = new Regex(@"\{\d+\}");
        foreach (var k in en.Keys)
        {
            var a = ph.Matches(en[k]).Select(m => m.Value).Distinct().OrderBy(x => x);
            var b = ph.Matches(ja[k]).Select(m => m.Value).Distinct().OrderBy(x => x);
            Assert.True(a.SequenceEqual(b), $"placeholder mismatch: {k}");
            Assert.False(string.IsNullOrWhiteSpace(ja[k]), k);
        }
    }

    [Fact]
    public void AllKeysUsedInSourceExist()
    {
        var en = Read(CultureInfo.InvariantCulture);
        var root = FindRepoRoot();
        var src = Path.Combine(root, "src", "WindowRescue");
        var keyPattern = new Regex(@"(?:Loc\.(?:T|F|Plural)\(\s*""|l:T\s+|\[)([A-Z][A-Za-z]+_[A-Za-z_]+)");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
                     .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xaml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in keyPattern.Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Value;
                if (m.Value.StartsWith("Loc.Plural")) { if (!en.ContainsKey(key + "_One") || !en.ContainsKey(key + "_Other")) missing.Add(key); }
                else if (!en.ContainsKey(key)) missing.Add($"{Path.GetFileName(file)}: {key}");
            }
        }
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("system", "ja-JP", "ja")]
    [InlineData("system", "en-US", "en")]
    [InlineData("system", "de-DE", "en")]
    [InlineData("ja", "en-US", "ja")]
    [InlineData("en", "ja-JP", "en")]
    public void ResolvesLanguage(string setting, string windows, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(windows)).TwoLetterISOLanguageName);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "WindowRescue"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}

[Trait("Category", "DesktopIntegration")]
public class SingleInstanceTests
{
    [Fact]
    public void CommandIsDeliveredToPrimaryInstance()
    {
        using var primary = new SingleInstance(TimeSpan.Zero);
        Assert.True(primary.IsPrimary, "WindowRescue.exe を終了してから実行してください。");
        using var delivered = new ManualResetEventSlim();
        AppCommand? received = null;
        primary.StartServer(c => { received = c; delivered.Set(); });
        var sent = new AppCommand(AppCommand.OpenList, -100, 200);
        Assert.True(SingleInstance.Send(sent));
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(sent, received);
    }

    [Fact]
    public void AnonymousAndUnknownCommandsAreRejectedWithoutStoppingServer()
    {
        using var primary = new SingleInstance(TimeSpan.Zero);
        Assert.True(primary.IsPrimary, "WindowRescue.exe を終了してから実行してください。");
        using var delivered = new ManualResetEventSlim();
        AppCommand? received = null;
        primary.StartServer(c => { received = c; delivered.Set(); });
        foreach (var (level, command) in new[]
        {
            (System.Security.Principal.TokenImpersonationLevel.Anonymous, "list|0|0"),
            (System.Security.Principal.TokenImpersonationLevel.Identification, "format-c|0|0"),
        })
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", SingleInstance.PipeName,
                System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.Asynchronous, level);
            client.Connect(2000);
            client.Write(System.Text.Encoding.UTF8.GetBytes(command));
            Assert.False(delivered.Wait(TimeSpan.FromMilliseconds(350)));
        }
        var sent = new AppCommand(AppCommand.OpenList, -100, 200);
        Assert.True(SingleInstance.Send(sent));
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(3)));
        Assert.Equal(sent, received);
    }
}
