using WindowRescue.Native;

namespace WindowRescue.Core;

/// <summary>接続中の画面の情報（物理ピクセル座標）。</summary>
public sealed record DisplayInfo(
    string Id,
    string DeviceName,
    string FriendlyName,
    PixelRect Bounds,
    PixelRect WorkArea,
    int Dpi,
    bool IsPrimary)
{
    public double Scale => Dpi / 96.0;

    /// <summary>\\.\DISPLAY2 → 2。Windows 側の番号として表示する。</summary>
    public int WindowsNumber
    {
        get
        {
            var digits = new string(DeviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }
    }
}

public static class MonitorService
{
    public static List<DisplayInfo> GetDisplays()
    {
        var names = DisplayConfig.GetTargetNames();
        var list = new List<DisplayInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = FromHandle(hMon, names);
            if (info != null) list.Add(info);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static DisplayInfo? FromHandle(IntPtr hMon, Dictionary<string, DisplayConfig.TargetName> names)
    {
        var mi = MONITORINFOEX.Create();
        if (!NativeMethods.GetMonitorInfo(hMon, ref mi)) return null;

        int dpi = 96;
        if (NativeMethods.GetDpiForMonitor(hMon, 0, out uint dx, out _) == 0 && dx > 0) dpi = (int)dx;

        string id, name;
        if (names.TryGetValue(mi.szDevice, out var tn) && !string.IsNullOrEmpty(tn.DevicePath))
        {
            id = tn.DevicePath;
            name = tn.FriendlyName;
        }
        else
        {
            // 代替：EnumDisplayDevices のモニターインターフェイス名
            var dd = DISPLAY_DEVICE.Create();
            if (NativeMethods.EnumDisplayDevices(mi.szDevice, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME) && !string.IsNullOrEmpty(dd.DeviceID))
            {
                id = dd.DeviceID;
                name = dd.DeviceString;
            }
            else
            {
                id = mi.szDevice;
                name = "";
            }
        }

        return new DisplayInfo(id, mi.szDevice, name,
            PixelRect.From(mi.rcMonitor), PixelRect.From(mi.rcWork), dpi,
            (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0);
    }

    public static DisplayInfo? FromPoint(IReadOnlyList<DisplayInfo> displays, int x, int y) =>
        displays.FirstOrDefault(d => d.Bounds.Contains(x, y));

    /// <summary>点を含む画面。なければ最も近い画面。</summary>
    public static DisplayInfo? NearestToPoint(IReadOnlyList<DisplayInfo> displays, int x, int y)
    {
        var hit = FromPoint(displays, x, y);
        if (hit != null) return hit;
        return displays.OrderBy(d => DistanceSq(d.Bounds, x, y)).FirstOrDefault();
    }

    /// <summary>矩形と最も重なる画面。重ならない場合は最も近い画面。</summary>
    public static DisplayInfo? NearestToRect(IReadOnlyList<DisplayInfo> displays, PixelRect r)
    {
        var best = displays.Select(d => (d, area: d.Bounds.Intersect(r).Area)).OrderByDescending(t => t.area).FirstOrDefault();
        if (best.d != null && best.area > 0) return best.d;
        return NearestToPoint(displays, r.CenterX, r.CenterY);
    }

    private static long DistanceSq(PixelRect r, int x, int y)
    {
        long dx = x < r.X ? r.X - x : x >= r.Right ? x - r.Right + 1 : 0;
        long dy = y < r.Y ? r.Y - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// GetWindowPlacement の rcNormalPosition は「ワークスペース座標」（プライマリ画面の作業領域の左上が原点）。
    /// スクリーン座標との差分を返す。
    /// </summary>
    public static (int dx, int dy) WorkspaceOffset(IReadOnlyList<DisplayInfo> displays)
    {
        var p = displays.FirstOrDefault(d => d.IsPrimary);
        return p == null ? (0, 0) : (p.WorkArea.X - p.Bounds.X, p.WorkArea.Y - p.Bounds.Y);
    }
}
