using System.Diagnostics;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Native;
using WindowRescue.Settings;
using WindowRescue.UI;

namespace WindowRescue;

/// <summary>常駐プロセスの中心。救出要求・ショートカット・監視・画面表示をまとめる。UI スレッドで使う。</summary>
public sealed class AppController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly HotkeyService _hotkeys;
    private readonly WindowWatcher _watcher;
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _displayChangeTimer;

    private RescuePanelWindow? _panel;
    private MiniWindow? _mini;
    private AboutWindow? _about;
    private WindowListWindow? _list;
    private SettingsWindow? _settingsWindow;

    private bool _busy;
    private Func<Task>? _pending;

    public AppSettings Settings { get; }
    public DisplayManager Displays { get; }
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WindowRescue.exe");
    public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>設定や表示に関わる状態が変わったとき（開いている画面の再描画用）。</summary>
    public event Action? StateChanged;
    public event Action? HotkeysChanged;

    public string? LastContextMenuError { get; private set; }

    public AppController(Dispatcher dispatcher, AppSettings settings)
    {
        _dispatcher = dispatcher;
        Settings = settings;
        Displays = new DisplayManager(settings);
        Displays.Refresh(MonitorService.GetDisplays(), Cursor());
        Save();

        _hotkeys = new HotkeyService();
        _hotkeys.Pressed += OnHotkey;
        ApplyHotkeys();

        _watcher = new WindowWatcher(dispatcher, CheckNewWindowAsync);
        ApplyMode();

        _tray = new TrayIcon(this);

        _displayChangeTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(800), DispatcherPriority.Background, (_, _) => OnDisplaysSettled(), dispatcher);
        _displayChangeTimer.Stop();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Loc.Instance.LanguageChanged += OnLanguageChanged;
    }

    // ───────────── 共通 ─────────────

    public static (int x, int y) Cursor()
    {
        var p = NativeMethods.CursorPos();
        return (p.X, p.Y);
    }

    /// <summary>カーソル位置の接続中画面。表示できない場合は、救出先・使用中画面・接続中画面の順に代替する（第5.1節）。</summary>
    public DisplayInfo UiDisplayAt((int x, int y) p)
    {
        var fresh = MonitorService.GetDisplays();
        return MonitorService.FromPoint(fresh, p.x, p.y)
            ?? MonitorService.FromPoint(fresh, Cursor().x, Cursor().y)
            ?? Displays.GetDestination(p)
            ?? Displays.Connected.FirstOrDefault()
            ?? fresh.First();
    }

    public void Save()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { Notify(null, StatusKind.Error, Loc.T("Error_SaveSettings"), ex.Message); }
    }

    public void RaiseStateChanged()
    {
        Save();
        StateChanged?.Invoke();
    }

    public void Notify(DisplayInfo? display, StatusKind kind, string title, string text = "")
    {
        display ??= Displays.GetDestination(Cursor()) ?? Displays.Connected.FirstOrDefault();
        ToastWindow.Show(display, kind, title, text);
    }

    public void ExecuteCommand(AppCommand cmd)
    {
        var p = (cmd.CursorX, cmd.CursorY);
        switch (cmd.Action)
        {
            case AppCommand.RescueHere: RescueAllHere(p); break;
            case AppCommand.OpenList: OpenList(p); break;
            case AppCommand.OpenSettings: OpenSettings(p); break;
            case AppCommand.OpenPanel: OpenPanel(p); break;
            case AppCommand.ShowMini: ShowMini(p, atCursor: true); break;
        }
    }

    // ───────────── 画面 ─────────────

    /// <summary>
    /// ミニウインドウを表示する。atCursor なら p（カーソル位置）に、そうでなければ前回の場所
    /// （なければ p の画面の右下）に出す。
    /// </summary>
    public void ShowMini((int x, int y) p, bool atCursor = false)
    {
        _mini ??= new MiniWindow(this);
        (int, int)? saved = Settings.MiniX is int x && Settings.MiniY is int y ? (x, y) : null;
        _mini.ShowAt(saved, UiDisplayAt(p), atCursor ? p : null);
        SetMiniVisible(true);
    }

    public void SetMiniVisible(bool visible)
    {
        if (Settings.ShowMini == visible) return;
        Settings.ShowMini = visible;
        Save();
    }

    public void SaveMiniPosition(int x, int y)
    {
        if (Settings.MiniX == x && Settings.MiniY == y) return;
        Settings.MiniX = x;
        Settings.MiniY = y;
        Save();
    }

    public void OpenAbout((int x, int y) p)
    {
        _about ??= CreateWindow(() => new AboutWindow(), () => _about = null);
        WindowPositioner.ShowOn(_about, UiDisplayAt(p));
    }

    public void OpenPanel((int x, int y) p)
    {
        _panel ??= CreateWindow(() => new RescuePanelWindow(this), () => _panel = null);
        _panel.OpenedOn = UiDisplayAt(p);
        WindowPositioner.ShowOn(_panel, _panel.OpenedOn);
        _panel.Refresh();
    }

    public void OpenList((int x, int y) p)
    {
        _list ??= CreateWindow(() => new WindowListWindow(this), () => _list = null);
        WindowPositioner.ShowOn(_list, UiDisplayAt(p));
        _list.Reload();
    }

    public void OpenSettings((int x, int y) p)
    {
        _settingsWindow ??= CreateWindow(() => new SettingsWindow(this), () => _settingsWindow = null);
        WindowPositioner.ShowOn(_settingsWindow, UiDisplayAt(p));
        _settingsWindow.Reload();
    }

    private static T CreateWindow<T>(Func<T> create, Action onClosed) where T : Window
    {
        var w = create();
        w.Closed += (_, _) => onClosed();
        return w;
    }

    // ───────────── 救出 ─────────────

    /// <summary>救出処理を 1 つずつ実行する。実行中の新しい要求は最新のものだけを残す（第13章）。</summary>
    private async void RunExclusive(Func<Task> work)
    {
        if (_busy)
        {
            _pending = work;
            return;
        }
        _busy = true;
        try
        {
            var next = work;
            while (next != null)
            {
                _pending = null;
                try { await next(); }
                catch (Exception ex) { Notify(null, StatusKind.Error, Loc.T("Error_Unexpected"), ex.Message); }
                next = _pending;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private bool IsExcludedForManual(WindowInfo w) => false;

    /// <summary>F-02：target へ全ウインドウを救出する。</summary>
    public void RescueAllTo(DisplayInfo target, DisplayInfo notifyOn)
    {
        RunExclusive(async () =>
        {
            var displays = Displays.Connected;
            var options = Settings.ToPlacementOptions();
            var report = await Task.Run(() => RescueEngine.RescueAll(target, displays, options, IsExcludedForManual, CancellationToken.None));
            ShowReport(report, notifyOn, Loc.F("Result_RescuedTo", Displays.NumberOf(target)));
            _list?.Reload();
        });
    }

    /// <summary>「この画面へ全ウインドウを救出」：ポインターのある画面へ集める。</summary>
    public void RescueAllHere((int x, int y) p)
    {
        var ui = UiDisplayAt(p);
        Displays.Refresh(MonitorService.GetDisplays(), p);
        var here = MonitorService.FromPoint(Displays.Connected, p.x, p.y) ?? ui;
        RescueAllTo(here, ui);
    }

    /// <summary>新しい窓を指定した画面に表示するか（表示先固定モードで、救出先がその画面）。</summary>
    public bool IsNewWindowsPinnedTo(DisplayInfo d) =>
        Settings.Mode == MonitorMode.FixedDestination && Displays.GetDestination(Cursor())?.Id == d.Id;

    /// <summary>
    /// 「新しい窓をこの画面に表示」。オンで表示先固定にする。
    /// オフでは「小さい窓を補正する」がオンなら自動救出、オフなら監視しない。
    /// </summary>
    public void SetNewWindowsPinned(DisplayInfo d, bool pinned, bool notify = true)
    {
        if (pinned)
        {
            Displays.SetDestination(d.Id);
            Settings.Mode = MonitorMode.FixedDestination;
        }
        else if (Settings.Mode == MonitorMode.FixedDestination)
        {
            Settings.Mode = Settings.AutoResize ? MonitorMode.AutoRescue : MonitorMode.Off;
        }
        ApplyMode();
        RaiseStateChanged();
        if (notify) Notify(d, StatusKind.Info, pinned ? Loc.F("Pin_On", Displays.NumberOf(d)) : Loc.T("Pin_Off"));
    }

    /// <summary>「小さい窓を補正する」がオンか（新しい極小の窓を自動で拡大する状態）。</summary>
    public bool IsTinyFixOn => Settings.AutoResize && Settings.Mode != MonitorMode.Off;

    /// <summary>今開いている極小の窓（サイズ変更可能な通常窓）をその場で補正する。ほかの窓は動かさない。</summary>
    public void FixTinyWindowsNow(DisplayInfo notifyOn)
    {
        RunExclusive(async () =>
        {
            var displays = Displays.Connected;
            var destination = Displays.GetDestination(Cursor()) ?? notifyOn;
            var options = Settings.ToPlacementOptions() with { FixTinySize = true };
            // 除外設定のアプリは「すべて」「サイズ補正だけ」どちらもサイズ補正しない
            var excluded = Settings.Exclusions.Select(e => e.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var report = await Task.Run(() =>
            {
                var rep = new RescueReport();
                foreach (var w in WindowEnumerator.Enumerate(displays))
                {
                    if (w.State == WindowShowState.Maximized || !w.Resizable || w.IsDialog || excluded.Contains(w.ProcessName)) continue;
                    var host = MonitorService.NearestToRect(displays, w.VisibleRect);
                    if (host == null || !PlacementCalculator.IsTiny(w.VisibleRect, host.Scale, options)) continue;
                    var (verdict, home) = WindowJudge.Judge(w.VisibleRect, w.State, true, false, null, displays, displays, options);
                    if (verdict == Verdict.Ok) continue;
                    rep.Results.Add(RescueEngine.ApplyVerdict(w, verdict, home, destination, displays, options, true));
                }
                return rep;
            });
            if (report.Results.Count == 0) Notify(notifyOn, StatusKind.Info, Loc.T("Tiny_None"));
            else ShowReport(report, notifyOn, Loc.T("Tiny_Fixed"));
            _list?.Reload();
        });
    }

    public void SetTinyFix(bool on)
    {
        Settings.AutoResize = on;
        // 表示先固定中はそのまま（固定モードでも極小の窓は補正される）
        if (Settings.Mode != MonitorMode.FixedDestination)
            Settings.Mode = on ? MonitorMode.AutoRescue : MonitorMode.Off;
        ApplyMode();
        RaiseStateChanged();
    }

    /// <summary>画面番号を指定した一括救出。未接続の画面では実行しない（第9章）。</summary>
    public void RescueAllToNumber(int n, (int x, int y) p)
    {
        var ui = UiDisplayAt(p);
        var target = ResolveNumberedTarget(n, ui);
        if (target != null) RescueAllTo(target, ui);
    }

    private DisplayInfo? ResolveNumberedTarget(int n, DisplayInfo ui)
    {
        Displays.Refresh(MonitorService.GetDisplays(), Cursor());
        var target = Displays.ByNumber(n);
        if (target == null) Notify(ui, StatusKind.Warning, Loc.F("Error_DisplayNotConnected", n));
        return target;
    }

    /// <summary>F-01：選択中（前面）の窓を画面 n へ移す。</summary>
    public void RescueForegroundToNumber(int n, (int x, int y) p)
    {
        var ui = UiDisplayAt(p);
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd != IntPtr.Zero) hwnd = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
        if (hwnd == IntPtr.Zero || !WindowEnumerator.IsTarget(hwnd))
        {
            Notify(ui, StatusKind.Warning, Loc.T("Error_NoActiveWindow"));
            return;
        }
        var target = ResolveNumberedTarget(n, ui);
        if (target == null) return;
        var w = WindowEnumerator.TryGet(hwnd, Displays.Connected);
        if (w == null) return;
        RescueWindows([w], target, ui, show: false, activateLast: false);
    }

    /// <summary>一覧などから個別に救出する。show=true なら最小化中の窓も表示し、最後の窓を操作対象にする。</summary>
    public void RescueWindows(IReadOnlyList<WindowInfo> windows, DisplayInfo target, DisplayInfo notifyOn, bool show, bool activateLast)
    {
        RunExclusive(async () =>
        {
            var displays = Displays.Connected;
            var options = Settings.ToPlacementOptions();
            var report = await Task.Run(() =>
            {
                var rep = new RescueReport();
                for (int i = 0; i < windows.Count; i++)
                {
                    bool last = i == windows.Count - 1;
                    rep.Results.Add(RescueEngine.RescueOne(windows[i], target, displays, options, show, activateLast && last));
                }
                return rep;
            });
            ShowReport(report, notifyOn, Loc.F("Result_RescuedTo", Displays.NumberOf(target)));
            _list?.Reload();
        });
    }

    private void ShowReport(RescueReport report, DisplayInfo notifyOn, string title)
    {
        string summary = Loc.F("Result_Summary", report.Moved, report.Skipped, report.Failed);
        var kind = report.Failed > 0 ? StatusKind.Error : report.Partial > 0 ? StatusKind.Warning : StatusKind.Success;
        var notes = report.Results.Where(r => r.Reason != null && r.Outcome != RescueOutcome.Skipped || r.Partial)
            .Take(3).Select(r => $"{r.Label}: {Loc.T(r.Reason ?? "Reason_DoesNotFit")}");
        string text = string.Join("\n", new[] { summary }.Concat(notes));
        Notify(notifyOn, kind, title, text);
        LastReport = report;
        StateChanged?.Invoke();
    }

    public RescueReport? LastReport { get; private set; }

    // ───────────── ショートカット ─────────────

    public HotkeyService Hotkeys => _hotkeys;

    public List<string> HotkeyActionIds => HotkeyActions.All(Displays.Connected.Count);

    public string GestureFor(string action) =>
        Settings.Hotkeys.TryGetValue(action, out var g) ? g : HotkeyActions.Default(action);

    public void ApplyHotkeys()
    {
        _hotkeys.Apply(HotkeyActionIds, GestureFor);
        HotkeysChanged?.Invoke();
    }

    private void OnHotkey(string action)
    {
        var p = Cursor();
        if (HotkeyActions.TryParseDisplayAction(action, out var a, out int n))
        {
            if (a == HotkeyActions.RescueForeground) RescueForegroundToNumber(n, p);
            else if (a == HotkeyActions.RescueAll) RescueAllToNumber(n, p);
            return;
        }
        switch (action)
        {
            case HotkeyActions.OpenList: OpenList(p); break;
            case HotkeyActions.RescueHere: RescueAllHere(p); break;
        }
    }

    // ───────────── 監視 ─────────────

    public void ApplyMode()
    {
        if (Settings.Mode == MonitorMode.Off) _watcher.Stop();
        else _watcher.Start();
    }

    private async Task<PixelRect?> CheckNewWindowAsync(IntPtr hwnd, bool recheck)
    {
        if (Settings.Mode == MonitorMode.Off) return null;
        var displays = Displays.Connected;
        var w = WindowEnumerator.TryGet(hwnd, displays);
        if (w == null) return null;
        var result = await CorrectAsync(w, Settings.Mode == MonitorMode.FixedDestination);
        return result;
    }

    /// <summary>1 窓を判定し、必要なら補正する。補正後の見えている矩形を返す。</summary>
    private async Task<PixelRect?> CorrectAsync(WindowInfo w, bool fixedDestination)
    {
        var exclusion = Settings.FindExclusion(w.ProcessName);
        if (exclusion?.Scope == ExclusionScope.All) return null;
        var destination = Displays.GetDestination(Cursor());
        if (destination == null) return null;

        var displays = Displays.Connected;
        var active = fixedDestination ? new List<DisplayInfo> { destination } : Displays.Connected.ToList();
        var options = Settings.ToPlacementOptions();
        bool allowResize = Settings.AutoResize && exclusion?.Scope != ExclusionScope.SizeOnly;
        if (!allowResize) options = options with { FixTinySize = false };

        PixelRect? owner = null;
        if (w.Owner != IntPtr.Zero && WindowEnumerator.TryGet(w.Owner, displays) is { } ow) owner = ow.VisibleRect;

        var (verdict, home) = WindowJudge.Judge(w.VisibleRect, w.State, w.Resizable, w.IsDialog, owner, active, displays, options);
        if (verdict == Verdict.Ok) return null;

        var r = await Task.Run(() => RescueEngine.ApplyVerdict(w, verdict, home, destination, displays, options, allowResize));
        if (r.Outcome != RescueOutcome.Moved) return null;
        return WindowEnumerator.TryGet(w.Handle, displays)?.VisibleRect;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _dispatcher.BeginInvoke(() => { _displayChangeTimer.Stop(); _displayChangeTimer.Start(); });

    private void OnDisplaysSettled()
    {
        _displayChangeTimer.Stop();
        Displays.Refresh(MonitorService.GetDisplays(), Cursor());
        ApplyHotkeys();
        RaiseStateChanged();
        InspectExistingWindows();
        _tray.RebuildMenu();
    }

    /// <summary>使用画面・接続構成の変更後、監視中なら既存の窓を 1 回点検する（第9章）。</summary>
    public void InspectExistingWindows()
    {
        if (Settings.Mode == MonitorMode.Off) return;
        RunExclusive(async () =>
        {
            var windows = WindowEnumerator.Enumerate(Displays.Connected);
            foreach (var w in windows) await CorrectAsync(w, false);
        });
    }

    // ───────────── 右クリックメニュー・言語・起動 ─────────────

    public ContextMenuState ContextMenuState => ContextMenuRegistrar.GetState(ExePath);

    public bool RegisterContextMenu()
    {
        try
        {
            ContextMenuRegistrar.Register(ExePath, Loc.T("Menu_Title"), Loc.T);
            Settings.ContextMenuWanted = true;
            LastContextMenuError = null;
            RaiseStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            LastContextMenuError = ex.Message;
            StateChanged?.Invoke();
            return false;
        }
    }

    public bool UnregisterContextMenu()
    {
        try
        {
            ContextMenuRegistrar.Unregister();
            Settings.ContextMenuWanted = false;
            LastContextMenuError = null;
            RaiseStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            LastContextMenuError = ex.Message;
            StateChanged?.Invoke();
            return false;
        }
    }

    public void SetLanguage(string setting)
    {
        Settings.Language = setting;
        Loc.Instance.SetLanguage(setting);
        RaiseStateChanged();
    }

    private void OnLanguageChanged()
    {
        _tray.RebuildMenu();
        // 登録済みの場合だけ、メニューの表示文字列を更新する（未登録なら登録しない）
        if (ContextMenuState != ContextMenuState.NotRegistered)
        {
            try
            {
                ContextMenuRegistrar.UpdateLabels(Loc.T("Menu_Title"), Loc.T);
            }
            catch (Exception ex)
            {
                LastContextMenuError = ex.Message;
                Notify(null, StatusKind.Warning, Loc.T("Error_ContextMenuLabels"), ex.Message);
            }
        }
        StateChanged?.Invoke();
    }

    public bool AutoStartEnabled => AutoStart.IsEnabled(ExePath);

    public void SetAutoStart(bool enabled)
    {
        try { AutoStart.Set(ExePath, enabled); }
        catch (Exception ex) { Notify(null, StatusKind.Error, Loc.T("Error_AutoStart"), ex.Message); }
        StateChanged?.Invoke();
    }

    /// <summary>管理者権限で再起動する（自動昇格はしない。利用者の操作でのみ呼ぶ）。</summary>
    public void RestartElevated()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, "--wait-instance --action settings") { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UAC で取り消された場合は何もしない
        }
    }

    public bool IsExiting { get; private set; }

    public void Exit()
    {
        IsExiting = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Loc.Instance.LanguageChanged -= OnLanguageChanged;
        _watcher.Dispose();
        _hotkeys.Dispose();
        _tray.Dispose();
    }
}
