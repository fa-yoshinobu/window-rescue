using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using System.Windows.Markup;
using WindowRescue.Native;

namespace WindowRescue.Localization;

/// <summary>
/// 表示言語の切替（第19章）。英語が基底リソース、日本語は Strings.ja.resx。
/// 言語を切り替えると Item[] の変更を通知し、開いている画面の表示も更新する。
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    private readonly ResourceManager _rm = new("WindowRescue.Resources.Strings", typeof(Loc).Assembly);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? LanguageChanged;

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    /// <summary>"system" のときに使う、Windows の利用者の表示言語。キーボード配列や地域からは推測しない。</summary>
    public static CultureInfo WindowsUiCulture
    {
        get
        {
            try { return CultureInfo.GetCultureInfo(NativeMethods.GetUserDefaultUILanguage()); }
            catch (CultureNotFoundException) { return CultureInfo.InstalledUICulture; }
        }
    }

    public static CultureInfo Resolve(string setting, CultureInfo windowsUi) => setting switch
    {
        "ja" => CultureInfo.GetCultureInfo("ja-JP"),
        "en" => CultureInfo.GetCultureInfo("en"),
        _ => windowsUi.TwoLetterISOLanguageName == "ja" ? CultureInfo.GetCultureInfo("ja-JP") : CultureInfo.GetCultureInfo("en"),
    };

    public void SetLanguage(string setting)
    {
        var culture = Resolve(setting, WindowsUiCulture);
        bool changed = culture.Name != Culture.Name;
        Culture = culture;
        // UI 言語だけを切り替え、数値・日付の書式（CurrentCulture＝地域設定）は変えない
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        if (changed)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            LanguageChanged?.Invoke();
        }
    }

    public bool IsJapanese => Culture.TwoLetterISOLanguageName == "ja";

    public string this[string key] => _rm.GetString(key, Culture) ?? _rm.GetString(key, CultureInfo.InvariantCulture) ?? key;

    public static string T(string key) => Instance[key];

    public static string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Instance[key], args);

    /// <summary>英語の単数・複数を扱う。key_One / key_Other を使い分ける。</summary>
    public static string Plural(string key, int count, params object[] args)
    {
        string k = count == 1 ? key + "_One" : key + "_Other";
        return string.Format(CultureInfo.CurrentCulture, Instance[k], [count, .. args]);
    }

    internal ResourceManager ResourceManager => _rm;
}

/// <summary>XAML 用：{l:T Key}。言語切替に追従する Binding を返す。</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
