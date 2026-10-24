using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Native;

namespace WindowRescue.UI;

/// <summary>
/// 画面に小さく常駐する操作ウインドウ。置かれている画面を「この画面」として、
/// 新しい窓の表示先・窓の一括救出・小さい窓の補正を操作する。
/// </summary>
public partial class MiniWindow : Window
{
    private readonly AppController _app;
    private readonly DispatcherTimer _moveTimer;
    private string? _lastDisplayId;
    private bool _updating;

    public MiniWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.StateChanged += Refresh;
        Loc.Instance.LanguageChanged += Refresh;
        // 移動が落ち着いてから位置を保存し、表示先を追従させる
        _moveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => OnMoved(), Dispatcher);
        _moveTimer.Stop();
        LocationChanged += (_, _) => { _moveTimer.Stop(); _moveTimer.Start(); };
        Closed += (_, _) => { _app.StateChanged -= Refresh; Loc.Instance.LanguageChanged -= Refresh; };
    }

    /// <summary>このウインドウがある画面。</summary>
    public DisplayInfo? Here => WindowPositioner.DisplayOf(this, MonitorService.GetDisplays());

    /// <summary>
    /// anchor があればその点（カーソル）の左上に、なければ保存した位置（なければ fallback の画面の右下）に表示する。
    /// </summary>
    public void ShowAt((int x, int y)? saved, DisplayInfo fallback, (int x, int y)? anchor = null)
    {
        if (anchor != null) saved = anchor;
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var displays = MonitorService.GetDisplays();
        bool useSaved = saved is { } s && displays.Any(d => d.WorkArea.Contains(s.x + 20, s.y + 20));
        var work = fallback.WorkArea;
        int x = useSaved ? saved!.Value.x : work.Right - (int)(300 * fallback.Scale);
        int y = useSaved ? saved!.Value.y : work.Bottom - (int)(220 * fallback.Scale);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        if (!IsVisible) Show();
        // 大きさが確定してから、作業領域からはみ出していれば戻す
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            NativeMethods.GetWindowRect(hwnd, out var r);
            var cur = PixelRect.From(r);
            if (anchor is { } a)
                cur = cur with { X = a.x - cur.Width, Y = a.y - cur.Height };
            var host = anchor is { } p
                ? MonitorService.NearestToPoint(MonitorService.GetDisplays(), p.x, p.y) ?? fallback
                : MonitorService.NearestToRect(MonitorService.GetDisplays(), cur) ?? fallback;
            var fit = PlacementCalculator.Clamp(cur, host.WorkArea);
            if (fit != PixelRect.From(r))
                NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, fit.X, fit.Y, 0, 0,
                    NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            _lastDisplayId = Here?.Id;
            Refresh();
        });
    }

    public void Refresh()
    {
        var here = Here;
        if (here == null) return;
        _app.Displays.Refresh(MonitorService.GetDisplays(), AppController.Cursor());
        DisplayText.Text = Loc.F("Mini_ThisDisplay", _app.Displays.NumberOf(here));
        // 表示の更新ではチェック変更の処理を動かさない
        _updating = true;
        PinCheck.IsChecked = _app.IsNewWindowsPinnedTo(here);
        TinyCheck.IsChecked = _app.IsTinyFixOn;
        _updating = false;
    }

    private void OnMoved()
    {
        _moveTimer.Stop();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.GetWindowRect(hwnd, out var r);
        _app.SaveMiniPosition(r.Left, r.Top);

        var here = Here;
        if (here == null || here.Id == _lastDisplayId) return;
        _lastDisplayId = here.Id;
        // 「新しい窓をこの画面に出す」がオンなら、移動先の画面へ表示先を変える
        if (_app.Settings.Mode == Settings.MonitorMode.FixedDestination)
            _app.SetNewWindowsPinned(here, true, notify: false);
        else
            Refresh();
    }

    // Click ではなく Checked/Unchecked で受け、キーボードや支援技術からの切り替えでも動くようにする
    private void Pin_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var here = Here;
        if (here != null) _app.SetNewWindowsPinned(here, PinCheck.IsChecked == true, notify: false);
    }

    private void Tiny_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        bool on = TinyCheck.IsChecked == true;
        _app.SetTinyFix(on);
        // オンにした瞬間に、今開いている小さい窓も補正する
        if (on && Here is { } here) _app.FixTinyWindowsNow(here);
    }

    private void Gather_Click(object sender, RoutedEventArgs e)
    {
        var here = Here;
        if (here != null) _app.RescueAllTo(here, here);
    }

    /// <summary>× で閉じても終了せず隠すだけにする（トレイから再表示できる）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _app.SetMiniVisible(false);
        }
        base.OnClosing(e);
    }
}
