using System.Text.Json;
using System.Text.Json.Serialization;
using WindowRescue.Core;

namespace WindowRescue.Settings;

public enum MonitorMode { Off, AutoRescue, FixedDestination }

public enum ExclusionScope { All, SizeOnly }

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
    public int Version { get; set; } = 1;
    public List<KnownDisplay> Displays { get; set; } = [];
    public string? DestinationId { get; set; }
    public MonitorMode Mode { get; set; } = MonitorMode.Off;
    public bool AutoResize { get; set; } = true;
    public int TinyWidthDip { get; set; } = 160;
    public int TinyHeightDip { get; set; } = 100;
    public int StandardWidthDip { get; set; } = 1000;
    public int StandardHeightDip { get; set; } = 700;
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

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 破損時は初期設定で起動する（既存ファイルは退避）
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch (Exception) { }
        }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Json));
        File.Move(tmp, FilePath, true);
    }
}
