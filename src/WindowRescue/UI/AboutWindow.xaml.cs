using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;
using WindowRescue.Localization;

namespace WindowRescue.UI;

/// <summary>バージョン情報（製品情報・使用ライブラリ・アプリのライセンス）。</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var app = typeof(AboutWindow).Assembly;
        ProductName = ProductOf(app);
        Version = AppVersion;
        Company = app.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? "FA Labo";
        Copyright = app.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "Copyright (c) 2026 FA Labo";
        using var stream = app.GetManifestResourceStream("WindowRescue.LICENSE");
        using var reader = stream is null ? null : new StreamReader(stream);
        LicenseText = reader?.ReadToEnd() ?? Loc.T("About_NoLicense");
        BuildLibraries();
        Loc.Instance.LanguageChanged += BuildLibraries;
        Closed += (_, _) => Loc.Instance.LanguageChanged -= BuildLibraries;
        DataContext = this;
    }

    /// <summary>アプリのバージョン（Directory.Build.props の Version）。</summary>
    public static string AppVersion => VersionOf(typeof(AboutWindow).Assembly);

    public static string ProductOf(Assembly a) => a.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Window Rescue";

    public string ProductName { get; }

    public string Version { get; }

    public string Company { get; }

    public string Copyright { get; }

    public string LicenseText { get; }

    public IReadOnlyList<LibraryInfo> Libraries { get; private set; } = [];

    private void BuildLibraries()
    {
        Libraries =
        [
            new(ProductName, Version, "MIT", Loc.T("About_PurposeApp")),
            new(Loc.T("About_DadsTokens"), "2.1.0", "MIT", Loc.T("About_PurposeDads")),
            new(".NET Runtime", Environment.Version.ToString(), "MIT", Loc.T("About_PurposeRuntime")),
        ];
        // 言語切替時に一覧を作り直す
        DataContext = null;
        DataContext = this;
    }

    private static string VersionOf(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(info) ? assembly.GetName().Version?.ToString() ?? "?" : info.Split('+')[0];
    }

    private void OpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(this, Loc.T("About_LinkError"), Loc.T("About_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    public sealed record LibraryInfo(string Name, string Version, string License, string Purpose);
}
