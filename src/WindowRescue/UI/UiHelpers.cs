using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Native;

namespace WindowRescue.UI;

/// <summary>本アプリの画面を、指定した画面（呼び出し時のカーソルのある画面）の中央に表示する（第5.1節）。</summary>
public static class WindowPositioner
{
    public static void ShowOn(Window w, DisplayInfo d, bool activate = true)
    {
        var hwnd = new WindowInteropHelper(w).EnsureHandle();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;

        Place(hwnd, d, w);
        if (!w.IsVisible) w.Show();
        // 拡大率の異なる画面へ移ると WPF が大きさを変えるため、レイアウト後にもう一度中央へ寄せる
        w.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Place(hwnd, d, w));
        if (activate)
        {
            w.Activate();
            NativeMethods.SetForegroundWindow(hwnd);
        }
    }

    private static void Place(IntPtr hwnd, DisplayInfo d, Window w)
    {
        var work = d.WorkArea;
        // 小さい画面では、作業領域に収まる最大サイズに制限する（DIP）
        w.MaxWidth = Math.Max(320, work.Width / d.Scale);
        w.MaxHeight = Math.Max(240, work.Height / d.Scale);

        NativeMethods.GetWindowRect(hwnd, out var r);
        var cur = PixelRect.From(r);
        // まず位置だけ移して DPI 変更を済ませる
        if (!d.Bounds.Contains(cur.CenterX, cur.CenterY))
        {
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, work.X + 16, work.Y + 16, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            NativeMethods.GetWindowRect(hwnd, out r);
            cur = PixelRect.From(r);
        }
        int width = Math.Min(cur.Width, work.Width);
        int height = Math.Min(cur.Height, work.Height);
        var target = PlacementCalculator.Center(width, height, work);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, target.X, target.Y, target.Width, target.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>ウインドウが現在表示されている画面。</summary>
    public static DisplayInfo? DisplayOf(Window w, IReadOnlyList<DisplayInfo> displays)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return null;
        NativeMethods.GetWindowRect(hwnd, out var r);
        return MonitorService.NearestToRect(displays, PixelRect.From(r));
    }
}

public enum StatusKind { Info, Success, Warning, Error }

/// <summary>DADS のノティフィケーションバナーに相当する状態表示。色だけでなくアイコンと文字で伝える。</summary>
public sealed class StatusBanner : Border
{
    private readonly TextBlock _icon = new() { FontSize = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _title = new() { FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };

    public StatusBanner()
    {
        BorderThickness = new Thickness(2);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(16, 12, 16, 12);
        Margin = new Thickness(0, 8, 0, 8);
        Visibility = Visibility.Collapsed;
        _icon.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Icon");
        _detail.SetResourceReference(TextBlock.FontSizeProperty, "Size.Small");
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSub");
        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(_text);
        stack.Children.Add(_detail);
        var dock = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        dock.Children.Add(_icon);
        dock.Children.Add(stack);
        Child = dock;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
    }

    public void Show(StatusKind kind, string title, string text = "", string? detail = null)
    {
        (string glyph, string fg, string bg, string kindKey) = kind switch
        {
            StatusKind.Success => ("", "Brush.Success", "Brush.SuccessBg", "Status_Success"),
            StatusKind.Warning => ("", "Brush.Warning", "Brush.WarningBg", "Status_Warning"),
            StatusKind.Error => ("", "Brush.Error", "Brush.ErrorBg", "Status_Error"),
            _ => ("", "Brush.Info", "Brush.InfoBg", "Status_Info"),
        };
        _icon.Text = glyph;
        _icon.SetResourceReference(TextBlock.ForegroundProperty, fg);
        SetResourceReference(BorderBrushProperty, fg);
        SetResourceReference(BackgroundProperty, bg);
        _title.Text = title;
        _text.Text = text;
        _text.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        _detail.Text = detail ?? "";
        _detail.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(this, $"{Loc.T(kindKey)}: {title} {text}");
        Visibility = Visibility.Visible;
        if (UIElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        else
            UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public void Hide() => Visibility = Visibility.Collapsed;
}

/// <summary>結果通知。フォーカスを奪わず、指定した画面の右下に数秒表示する（第13章）。</summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer;

    private ToastWindow(StatusKind kind, string title, string text)
    {
        Style = (Style)Application.Current.FindResource("AppWindow");
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 440;
        Focusable = false;
        SetResourceReference(BorderBrushProperty, "Brush.Border");
        BorderThickness = new Thickness(1);

        var banner = new StatusBanner { Margin = new Thickness(0), MinWidth = 320 };
        banner.Show(kind, title, text);
        Content = banner;
        MouseLeftButtonUp += (_, _) => Close();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == StatusKind.Error ? 8 : 5) };
        _timer.Tick += (_, _) => Close();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            long ex = NativeMethods.GetExStyle(hwnd) | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        };
        Closed += (_, _) => { _timer.Stop(); if (_current == this) _current = null; };
    }

    public static void Show(DisplayInfo? display, StatusKind kind, string title, string text)
    {
        _current?.Close();
        var t = new ToastWindow(kind, title, text);
        _current = t;
        var hwnd = new WindowInteropHelper(t).EnsureHandle();
        if (display != null)
        {
            var work = display.WorkArea;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, work.X + 16, work.Y + 16, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
        t.Show();
        t.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (display == null) return;
            NativeMethods.GetWindowRect(hwnd, out var r);
            var cur = PixelRect.From(r);
            int margin = (int)(16 * display.Scale);
            var work = display.WorkArea;
            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, work.Right - cur.Width - margin, work.Bottom - cur.Height - margin, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        });
        t._timer.Start();
    }
}
