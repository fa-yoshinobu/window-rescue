using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Settings;

namespace WindowRescue;

public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var cursor = AppController.Cursor();
        var cmd = AppCommand.FromArgs(e.Args, cursor.x, cursor.y);
        bool waitForInstance = e.Args.Contains("--wait-instance");
        bool background = e.Args.Contains("--background");

        _instance = new SingleInstance(waitForInstance ? TimeSpan.FromSeconds(15) : TimeSpan.Zero);
        if (!_instance.IsPrimary)
        {
            // 常駐中のプロセスへ操作を渡し、こちらは終了する（重複起動しない）
            if (!background)
                SingleInstance.Send(cmd.Action == AppCommand.None ? cmd with { Action = AppCommand.ShowMini } : cmd);
            Shutdown();
            return;
        }

        var settings = SettingsStore.Load();
        Loc.Instance.SetLanguage(settings.Language);
        ApplyHighContrast();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;

        _controller = new AppController(Dispatcher, settings);
        _instance.StartServer(c => Dispatcher.BeginInvoke(() => _controller.ExecuteCommand(c)));

        // ミニウインドウを表示する（× で隠した場合は、明示的に起動したときだけ再表示）。既存の窓は動かさない
        if (settings.ShowMini || (!background && cmd.Action == AppCommand.None)) _controller.ShowMini(cursor);
        if (cmd.Action != AppCommand.None) _controller.ExecuteCommand(cmd);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        _controller?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) ApplyHighContrast();
    }

    /// <summary>ハイコントラスト時は OS の配色を尊重する（第17.2節）。</summary>
    private void ApplyHighContrast()
    {
        var keys = new[]
        {
            "Brush.Background", "Brush.Surface", "Brush.Text", "Brush.TextSub", "Brush.Border", "Brush.Divider",
            "Brush.Primary", "Brush.PrimaryHover", "Brush.PrimaryPressed", "Brush.OnPrimary", "Brush.SecondaryHover",
            "Brush.SecondaryPressed", "Brush.Selected", "Brush.DisabledBg", "Brush.DisabledFg", "Brush.Success",
            "Brush.SuccessBg", "Brush.Error", "Brush.ErrorBg", "Brush.Warning", "Brush.WarningBg", "Brush.Info",
            "Brush.InfoBg", "Brush.FocusOuter", "Brush.FocusInner",
        };
        if (!SystemParameters.HighContrast)
        {
            foreach (var k in keys) Resources.Remove(k);   // Theme.xaml の値に戻す
            return;
        }
        Brush bg = SystemColors.WindowBrush, fg = SystemColors.WindowTextBrush;
        Brush hl = SystemColors.HighlightBrush, hlText = SystemColors.HighlightTextBrush, gray = SystemColors.GrayTextBrush;
        var map = new Dictionary<string, Brush>
        {
            ["Brush.Background"] = bg, ["Brush.Surface"] = bg, ["Brush.Text"] = fg, ["Brush.TextSub"] = fg,
            ["Brush.Border"] = fg, ["Brush.Divider"] = fg, ["Brush.Primary"] = hl, ["Brush.PrimaryHover"] = hl,
            ["Brush.PrimaryPressed"] = hl, ["Brush.OnPrimary"] = hlText, ["Brush.SecondaryHover"] = bg,
            ["Brush.SecondaryPressed"] = bg, ["Brush.Selected"] = hl, ["Brush.DisabledBg"] = bg, ["Brush.DisabledFg"] = gray,
            ["Brush.Success"] = fg, ["Brush.SuccessBg"] = bg, ["Brush.Error"] = fg, ["Brush.ErrorBg"] = bg,
            ["Brush.Warning"] = fg, ["Brush.WarningBg"] = bg, ["Brush.Info"] = fg, ["Brush.InfoBg"] = bg,
            ["Brush.FocusOuter"] = hl, ["Brush.FocusInner"] = fg,
        };
        foreach (var (k, v) in map) Resources[k] = v;
    }
}
