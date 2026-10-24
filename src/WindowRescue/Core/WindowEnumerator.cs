using System.Diagnostics;
using System.Text;
using WindowRescue.Native;

namespace WindowRescue.Core;

public enum WindowShowState { Normal, Minimized, Maximized }

/// <summary>対象ウインドウのスナップショット。座標はすべて物理ピクセルのスクリーン座標。</summary>
public sealed class WindowInfo
{
    public required IntPtr Handle { get; init; }
    public required uint ProcessId { get; init; }
    public required long ProcessStartTime { get; init; }
    public required string ProcessName { get; init; }
    public required string AppName { get; init; }
    public required string Title { get; init; }
    public required string ClassName { get; init; }
    public required IntPtr Owner { get; init; }
    public required WindowShowState State { get; init; }
    public required bool RestoreToMaximized { get; init; }
    /// <summary>現在のウインドウ矩形（最小化中は通常表示時の矩形）。</summary>
    public required PixelRect WindowRect { get; init; }
    /// <summary>見えている枠の矩形（影・透明な枠を除く）。</summary>
    public required PixelRect VisibleRect { get; init; }
    /// <summary>通常表示へ戻したときの矩形（スクリーン座標）。</summary>
    public required PixelRect NormalRect { get; init; }
    public required bool Resizable { get; init; }
    public required bool IsHung { get; init; }

    public bool IsDialog => Owner != IntPtr.Zero || ClassName == "#32770";
    public FrameMargins Margins => FrameMargins.Between(WindowRect, VisibleRect);
    public string DisplayLabel => string.IsNullOrWhiteSpace(Title) ? $"({ProcessName})" : Title;
}

public static class WindowEnumerator
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow",
        "#32768", "tooltips_class32", "Xaml_WindowedPopupClass", "ForegroundStaging", "MultitaskingViewFrame",
        "SysShadow", "Shell_InputSwitchTopLevelWindow", "TaskListThumbnailWnd", "NotifyIconOverflowWindow",
    };

    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static readonly Dictionary<string, string> AppNameCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>第11章の対象ウインドウか（本アプリ自身は除く）。</summary>
    public static bool IsTarget(IntPtr hwnd, bool includeOwnProcess = false)
    {
        if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd)) return false;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return false;

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (!includeOwnProcess && pid == OwnPid) return false;

        long style = NativeMethods.GetStyle(hwnd);
        long ex = NativeMethods.GetExStyle(hwnd);
        if ((style & NativeMethods.WS_CHILD) != 0) return false;
        if ((ex & NativeMethods.WS_EX_NOACTIVATE) != 0) return false;
        if ((ex & NativeMethods.WS_EX_TOOLWINDOW) != 0 && (ex & NativeMethods.WS_EX_APPWINDOW) == 0) return false;

        bool hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        bool hasFrame = (style & NativeMethods.WS_THICKFRAME) != 0;
        bool appWindow = (ex & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (!hasCaption && !hasFrame && !appWindow) return false;

        // 別の仮想デスクトップや OS が隠している窓（クローク）は対象外
        if (NativeMethods.IsCloaked(hwnd)) return false;

        if (ShellClasses.Contains(NativeMethods.GetClass(hwnd))) return false;

        if (!NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.GetWindowRect(hwnd, out var r);
            if (r.Right - r.Left <= 0 || r.Bottom - r.Top <= 0) return false;
        }
        return true;
    }

    public static List<WindowInfo> Enumerate(IReadOnlyList<DisplayInfo> displays)
    {
        var handles = new List<IntPtr>();
        NativeMethods.EnumWindows((h, _) => { if (IsTarget(h)) handles.Add(h); return true; }, IntPtr.Zero);
        var list = new List<WindowInfo>(handles.Count);
        foreach (var h in handles)
        {
            var info = TryGet(h, displays);
            if (info != null) list.Add(info);
        }
        return list;
    }

    public static WindowInfo? TryGet(IntPtr hwnd, IReadOnlyList<DisplayInfo> displays)
    {
        if (!NativeMethods.IsWindow(hwnd)) return null;
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        var (path, start) = QueryProcess(pid);
        string procName = path.Length > 0 ? Path.GetFileNameWithoutExtension(path) : $"PID {pid}";

        var wp = WINDOWPLACEMENT.Create();
        if (!NativeMethods.GetWindowPlacement(hwnd, ref wp)) return null;
        var (ox, oy) = MonitorService.WorkspaceOffset(displays);
        var normal = PixelRect.From(wp.rcNormalPosition).Offset(ox, oy);

        bool iconic = NativeMethods.IsIconic(hwnd);
        bool zoomed = NativeMethods.IsZoomed(hwnd);
        var state = iconic ? WindowShowState.Minimized : zoomed ? WindowShowState.Maximized : WindowShowState.Normal;

        PixelRect windowRect, visible;
        if (iconic)
        {
            windowRect = visible = normal;
        }
        else
        {
            NativeMethods.GetWindowRect(hwnd, out var wr);
            windowRect = PixelRect.From(wr);
            visible = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out RECT fr, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0
                ? PixelRect.From(fr) : windowRect;
            if (visible.IsEmpty) visible = windowRect;
        }

        long style = NativeMethods.GetStyle(hwnd);
        return new WindowInfo
        {
            Handle = hwnd,
            ProcessId = pid,
            ProcessStartTime = start,
            ProcessName = procName,
            AppName = GetAppName(path, procName),
            Title = NativeMethods.GetText(hwnd),
            ClassName = NativeMethods.GetClass(hwnd),
            Owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER),
            State = state,
            RestoreToMaximized = (wp.flags & NativeMethods.WPF_RESTORETOMAXIMIZED) != 0,
            WindowRect = windowRect,
            VisibleRect = visible,
            NormalRect = normal,
            Resizable = (style & NativeMethods.WS_THICKFRAME) != 0,
            IsHung = NativeMethods.IsHungAppWindow(hwnd),
        };
    }

    /// <summary>実行ファイルのパスと開始時刻。権限の高いプロセスでも取得できる限定アクセスを使う。</summary>
    public static (string path, long startTime) QueryProcess(uint pid)
    {
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return ("", 0);
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            string path = NativeMethods.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : "";
            long start = NativeMethods.GetProcessTimes(h, out long c, out _, out _, out _) ? c : 0;
            return (path, start);
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }

    private static string GetAppName(string path, string fallback)
    {
        if (path.Length == 0) return fallback;
        lock (AppNameCache)
        {
            if (AppNameCache.TryGetValue(path, out var cached)) return cached;
            string name = fallback;
            try
            {
                var desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim();
            }
            catch (Exception)
            {
                // アクセスできない場合はプロセス名を使う
            }
            AppNameCache[path] = name;
            return name;
        }
    }
}
