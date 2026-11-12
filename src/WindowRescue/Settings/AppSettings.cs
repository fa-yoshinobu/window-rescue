using System.Text.Json;
using System.Text.Json.Serialization;
using WindowRescue.Core;

namespace WindowRescue.Settings;

public enum MonitorMode { Off, AutoRescue, FixedDestination }

public enum ExclusionScope { All, SizeOnly }

internal readonly record struct SizeRange(int Min, int Max, int Default)
{
    public bool Contains(int value) => value >= Min && value <= Max;
    public int Validate(int value) => Contains(value) ? value : Default;
    public bool TryParse(string text, out int value) => int.TryParse(text.Trim(), out value) && Contains(value);
}

internal static class SettingRanges
{
    public static readonly SizeRange TinyWidth = new(20, 2000, 160);
    public static readonly SizeRange TinyHeight = new(20, 2000, 100);
    public static readonly SizeRange StandardWidth = new(200, 8000, 1000);
    public static readonly SizeRange StandardHeight = new(150, 8000, 700);
}

public sealed class KnownDisplay
{
    public string Id { get; set; } = "";
    /// <summary>本アプリの画面番号（ショートカットの 1／2／3 に対応）。</summary>
    public int Number { get; set; }
    public string Name { get; set; } = "";
}

public sealed class AppExclusion
{
    public string ProcessName { get; set; } = "";
    public ExclusionScope Scope { get; set; } = ExclusionScope.All;
}

public sealed class AppSettings
{
    public const int CurrentVersion = 1;
    public int Version { get; set; } = CurrentVersion;
    [JsonIgnore]
    public bool IsReadOnly { get; internal set; }
    public List<KnownDisplay> Displays { get; set; } = [];
    public string? DestinationId { get; set; }
    public MonitorMode Mode { get; set; } = MonitorMode.Off;
    public bool AutoResize { get; set; } = true;
    public int TinyWidthDip { get; set; } = SettingRanges.TinyWidth.Default;
    public int TinyHeightDip { get; set; } = SettingRanges.TinyHeight.Default;
    public int StandardWidthDip { get; set; } = SettingRanges.StandardWidth.Default;
    public int StandardHeightDip { get; set; } = SettingRanges.StandardHeight.Default;
    public List<AppExclusion> Exclusions { get; set; } = [];
    /// <summary>操作 ID → キー（例 "Ctrl+Alt+1"）。空文字は割り当てなし。</summary>
    public Dictionary<string, string> Hotkeys { get; set; } = [];
    /// <summary>"system" / "ja" / "en"</summary>
    public string Language { get; set; } = "system";
    /// <summary>ミニウインドウを表示するか（× で隠すと false）。</summary>
    public bool ShowMini { get; set; } = true;
    /// <summary>ミニウインドウの位置（物理ピクセルのスクリーン座標）。</summary>
    public int? MiniX { get; set; }
    public int? MiniY { get; set; }
    /// <summary>右クリックメニューを登録した意思（実際の登録状態はレジストリで確認する）。</summary>
    public bool ContextMenuWanted { get; set; }

    public PlacementOptions ToPlacementOptions() => new()
    {
        TinyWidthDip = TinyWidthDip,
        TinyHeightDip = TinyHeightDip,
        StandardWidthDip = StandardWidthDip,
        StandardHeightDip = StandardHeightDip,
        FixTinySize = AutoResize,
    };

    public AppExclusion? FindExclusion(string processName) =>
        Exclusions.FirstOrDefault(e => string.Equals(e.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowRescue");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    public static bool Exists => File.Exists(FilePath);

    public static AppSettings Load() => Load(FilePath);

    internal static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                using var document = JsonDocument.Parse(text);
                // 新しい版は未知の項目・列挙値を逆直列化する前に保護する。今回だけ既定値で動く。
                if (document.RootElement.TryGetProperty("Version", out var version)
                    && version.TryGetInt32(out int number) && number > AppSettings.CurrentVersion)
                    return new AppSettings { Version = number, IsReadOnly = true };
                return MigrateAndValidate(JsonSerializer.Deserialize<AppSettings>(text, Json) ?? new AppSettings());
            }
        }
        catch (Exception ex)
        {
            Log.Error("Load settings", ex);
            // 破損時は初期設定で起動する（既存ファイルは退避）
            try { File.Copy(path, path + ".broken", true); } catch (Exception) { }
        }
        return new AppSettings();
    }

    private static AppSettings MigrateAndValidate(AppSettings s)
    {
        // 現在公開済みの形式は1だけ。形式を変えるときは旧版から順に移行するcaseをここへ追加する。
        switch (s.Version)
        {
            case AppSettings.CurrentVersion:
                break;
            default:
                throw new JsonException("Unsupported settings version");
        }
        s.TinyWidthDip = SettingRanges.TinyWidth.Validate(s.TinyWidthDip);
        s.TinyHeightDip = SettingRanges.TinyHeight.Validate(s.TinyHeightDip);
        s.StandardWidthDip = SettingRanges.StandardWidth.Validate(s.StandardWidthDip);
        s.StandardHeightDip = SettingRanges.StandardHeight.Validate(s.StandardHeightDip);
        return s;
    }

    public static void Save(AppSettings s) => Save(s, FilePath);

    internal static void Save(AppSettings s, string path)
    {
        if (s.IsReadOnly) return;
        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
            File.Move(tmp, path, true);
        }
        catch (Exception ex)
        {
            Log.Error("Save settings", ex);
            throw;
        }
    }
}
