using System.IO;
using WindowRescue.Settings;

namespace WindowRescue.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "WindowRescue-settings-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    public SettingsTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Theory]
    [InlineData(-5, 0, 199, 149)]
    [InlineData(2001, 2001, 8001, 8001)]
    public void InvalidSizesUseDefaults(int tw, int th, int sw, int sh)
    {
        File.WriteAllText(FilePath, $$"""{"Version":1,"TinyWidthDip":{{tw}},"TinyHeightDip":{{th}},"StandardWidthDip":{{sw}},"StandardHeightDip":{{sh}}} """);
        var settings = SettingsStore.Load(FilePath);
        Assert.Equal((160, 100, 1000, 700), (settings.TinyWidthDip, settings.TinyHeightDip, settings.StandardWidthDip, settings.StandardHeightDip));
    }

    [Theory]
    [InlineData(20, 20, 200, 150)]
    [InlineData(2000, 2000, 8000, 8000)]
    public void LoadAndEditorAcceptTheSameBoundaries(int tw, int th, int sw, int sh)
    {
        var original = new AppSettings { TinyWidthDip = tw, TinyHeightDip = th, StandardWidthDip = sw, StandardHeightDip = sh };
        SettingsStore.Save(original, FilePath);
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal((tw, th, sw, sh), (loaded.TinyWidthDip, loaded.TinyHeightDip, loaded.StandardWidthDip, loaded.StandardHeightDip));
        foreach (var (range, value) in new[] { (SettingRanges.TinyWidth, tw), (SettingRanges.TinyHeight, th), (SettingRanges.StandardWidth, sw), (SettingRanges.StandardHeight, sh) })
        {
            Assert.True(range.TryParse(value.ToString(), out _));
            Assert.False(range.TryParse((range.Min - 1).ToString(), out _));
            Assert.False(range.TryParse((range.Max + 1).ToString(), out _));
        }
    }

    [Fact]
    public void SupportedAndMissingVersionsKeepExistingValues()
    {
        foreach (string version in new[] { "\"Version\":1,", "" })
        {
            File.WriteAllText(FilePath, "{" + version + "\"TinyWidthDip\":250,\"Hotkeys\":{\"OpenList\":\"\"}}");
            var loaded = SettingsStore.Load(FilePath);
            Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
            Assert.False(loaded.IsReadOnly);
            Assert.Equal(250, loaded.TinyWidthDip);
            Assert.Equal("", loaded.Hotkeys["OpenList"]);
        }
    }

    [Fact]
    public void FutureSchemaNeverOverwritesOriginalEvenWithUnknownEnum()
    {
        const string future = """{"Version":99,"Mode":"FutureMode","FutureField":{"value":123}}""";
        File.WriteAllText(FilePath, future);
        var loaded = SettingsStore.Load(FilePath);
        Assert.True(loaded.IsReadOnly);
        Assert.Equal(99, loaded.Version);
        Assert.Equal(MonitorMode.Off, loaded.Mode);
        loaded.TinyWidthDip = 500;
        SettingsStore.Save(loaded, FilePath);
        Assert.Equal(future, File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".broken"));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void UnsupportedOldVersionUsesDefaultsAndKeepsBackup()
    {
        const string invalid = """{"Version":-1,"TinyWidthDip":350}""";
        File.WriteAllText(FilePath, invalid);
        var loaded = SettingsStore.Load(FilePath);
        Assert.Equal(AppSettings.CurrentVersion, loaded.Version);
        Assert.Equal(160, loaded.TinyWidthDip);
        Assert.Equal(invalid, File.ReadAllText(FilePath + ".broken"));
    }
}
