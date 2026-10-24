using System.Runtime.InteropServices;
using WindowRescue.Native;

namespace WindowRescue.Core;

public enum RescueOutcome { Moved, Unchanged, Skipped, Failed }

/// <summary>1 窓の処理結果。Reason はリソースキー、Detail は OS 由来の原文。</summary>
public sealed record WindowResult(IntPtr Handle, string Label, RescueOutcome Outcome, string? Reason = null, string? Detail = null, bool Partial = false);

public sealed class RescueReport
{
    public List<WindowResult> Results { get; } = [];
    public int Moved => Results.Count(r => r.Outcome is RescueOutcome.Moved or RescueOutcome.Unchanged);
    public int Skipped => Results.Count(r => r.Outcome == RescueOutcome.Skipped);
    public int Failed => Results.Count(r => r.Outcome == RescueOutcome.Failed);
    public int Partial => Results.Count(r => r.Partial);
}

public enum PlaceMode
{
    /// <summary>救出先に重なっていれば最小限の移動、なければ中央。</summary>
    KeepOrCenter,
    /// <summary>救出先に収まっていればそのまま、なければ段違い配置（一括救出）。</summary>
    Cascade,
}

/// <summary>ウインドウの移動・復元を行う。UI スレッド以外から呼ぶ。</summary>
public static class RescueEngine
{
    private static readonly TimeSpan PerWindowTimeout = TimeSpan.FromSeconds(2);
    private const uint MoveFlags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER;

    /// <summary>F-02：全対象ウインドウを target へ集める。</summary>
    public static RescueReport RescueAll(DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        Func<WindowInfo, bool> isExcluded, CancellationToken ct)
    {
        var report = new RescueReport();
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        var windows = WindowEnumerator.Enumerate(displays);
        var newVisible = new Dictionary<IntPtr, PixelRect>();
        int cascade = 0;
        bool touchedMaximized = false;

        // 親を先に動かし、ダイアログは親の新しい位置に重ねる
        var ordered = windows.Where(w => !HasTargetOwner(w, windows))
            .Concat(windows.Where(w => HasTargetOwner(w, windows)));

        foreach (var w in ordered)
        {
            if (ct.IsCancellationRequested) break;
            if (isExcluded(w))
            {
                report.Results.Add(new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Skipped, "Reason_Excluded"));
                continue;
            }
            PixelRect? owner = w.Owner != IntPtr.Zero && newVisible.TryGetValue(w.Owner, out var ov) ? ov : null;
            bool usesCascade = owner == null && !(w.State == WindowShowState.Normal && target.WorkArea.Contains(w.VisibleRect));
            var result = WithTimeout(w, () => Move(w, target, displays, o, PlaceMode.Cascade, cascade, owner, false));
            if (usesCascade && result.Outcome == RescueOutcome.Moved) cascade++;
            if (w.State == WindowShowState.Maximized) touchedMaximized = true;
            report.Results.Add(result);

            if (result.Outcome is RescueOutcome.Moved or RescueOutcome.Unchanged)
            {
                var now = WindowEnumerator.TryGet(w.Handle, displays);
                if (now != null) newVisible[w.Handle] = now.VisibleRect;
            }
        }

        // 最大化の復元で前面が変わった場合は、操作中だった窓へ戻す
        if (touchedMaximized && foreground != IntPtr.Zero && NativeMethods.IsWindow(foreground)
            && NativeMethods.GetForegroundWindow() != foreground)
            NativeMethods.SetForegroundWindow(foreground);
        return report;
    }

    private static bool HasTargetOwner(WindowInfo w, List<WindowInfo> all) =>
        w.Owner != IntPtr.Zero && all.Any(x => x.Handle == w.Owner);

    /// <summary>1 窓を target へ移す。show=true なら最小化中でも通常表示に戻して前面に出す（一覧からの個別救出）。</summary>
    public static WindowResult RescueOne(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays,
        PlacementOptions o, bool show, bool activate)
    {
        PixelRect? owner = null;
        if (w.Owner != IntPtr.Zero && WindowEnumerator.TryGet(w.Owner, displays) is { } ow
            && ow.State == WindowShowState.Normal && target.WorkArea.Intersect(ow.VisibleRect).Area > 0)
            owner = ow.VisibleRect;
        var r = WithTimeout(w, () => Move(w, target, displays, o, PlaceMode.KeepOrCenter, 0, owner, show));
        if (activate && r.Outcome is RescueOutcome.Moved or RescueOutcome.Unchanged)
            NativeMethods.SetForegroundWindow(w.Handle);
        return r;
    }

    /// <summary>自動補正（第7章）。フォーカスを変えない。</summary>
    public static WindowResult ApplyVerdict(WindowInfo w, Verdict verdict, DisplayInfo? home, DisplayInfo destination,
        IReadOnlyList<DisplayInfo> displays, PlacementOptions o, bool allowResize)
    {
        var opts = allowResize ? o : o with { FixTinySize = false };
        return verdict switch
        {
            Verdict.MoveToDestination => RescueOne(w, destination, displays, opts, false, false),
            Verdict.FitInDisplay or Verdict.EnlargeInDisplay when home != null =>
                WithTimeout(w, () => Move(w, home, displays, opts, PlaceMode.KeepOrCenter, 0, null, false)),
            _ => new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Unchanged),
        };
    }

    private static WindowResult WithTimeout(WindowInfo w, Func<WindowResult> action)
    {
        if (w.IsHung) return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_NotResponding");
        var task = Task.Run(action);
        try
        {
            if (task.Wait(PerWindowTimeout)) return task.Result;
        }
        catch (AggregateException ex)
        {
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_Unexpected", ex.InnerException?.Message);
        }
        return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_NotResponding");
    }

    private static WindowResult Move(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, bool show)
    {
        if (!NativeMethods.IsWindow(w.Handle))
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Skipped, "Reason_Closed");

        WindowResult result = w.State switch
        {
            WindowShowState.Normal => MoveNormal(w, target, displays, o, mode, cascadeIndex, ownerVisible),
            _ => MoveByPlacement(w, target, displays, o, mode, cascadeIndex, ownerVisible, show),
        };

        // 個別救出：最小化中の窓は通常表示へ戻す（上で位置は変更済み）
        if (show && result.Outcome != RescueOutcome.Failed && NativeMethods.IsIconic(w.Handle))
            NativeMethods.ShowWindow(w.Handle, NativeMethods.SW_RESTORE);

        return result;
    }

    private static PixelRect ComputeTarget(int width, int height, PixelRect current, bool resizable, bool allowTinyFix,
        DisplayInfo target, PlacementOptions o, PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, out bool resized)
    {
        var work = target.WorkArea;
        var (w, h, rs) = PlacementCalculator.TargetSize(width, height, target.Scale, work, resizable, allowTinyFix, o);
        resized = rs;
        if (ownerVisible is { } ov) return PlacementCalculator.CenterOver(w, h, ov, work);

        var sized = current with { Width = w, Height = h };
        if (mode == PlaceMode.Cascade)
        {
            if (work.Contains(current)) return PlacementCalculator.Clamp(sized, work);
            return PlacementCalculator.Cascade(w, h, work, cascadeIndex, (int)Math.Round(o.CascadeStepDip * target.Scale));
        }
        return work.Intersect(current).Area > 0 ? PlacementCalculator.Clamp(sized, work) : PlacementCalculator.Center(w, h, work);
    }

    private static WindowResult MoveNormal(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible)
    {
        var info = w;
        var source = MonitorService.NearestToRect(displays, w.VisibleRect);

        // 拡大率が異なる画面へは、先に位置だけ移してアプリ自身の DPI 変更処理を済ませる
        if (source != null && source.Dpi != target.Dpi && target.WorkArea.Intersect(w.VisibleRect).Area == 0)
        {
            var pre = PlacementCalculator.Center(Math.Min(w.WindowRect.Width, target.WorkArea.Width),
                Math.Min(w.WindowRect.Height, target.WorkArea.Height), target.WorkArea);
            if (!NativeMethods.SetWindowPos(w.Handle, IntPtr.Zero, pre.X, pre.Y, 0, 0, MoveFlags | NativeMethods.SWP_NOSIZE))
                return Failure(w);
            info = WindowEnumerator.TryGet(w.Handle, displays) ?? w;
        }

        var visible = info.VisibleRect;
        var newVisible = ComputeTarget(visible.Width, visible.Height, visible, info.Resizable, !info.IsDialog,
            target, o, mode, cascadeIndex, ownerVisible, out bool resized);
        var newWindow = info.Margins.ToWindowRect(newVisible);

        if (newWindow == info.WindowRect && info == w)
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Unchanged);

        uint flags = MoveFlags | (resized || newWindow.Width != info.WindowRect.Width || newWindow.Height != info.WindowRect.Height ? 0 : NativeMethods.SWP_NOSIZE);
        if (!NativeMethods.SetWindowPos(w.Handle, IntPtr.Zero, newWindow.X, newWindow.Y, newWindow.Width, newWindow.Height, flags))
            return Failure(w);

        var after = WindowEnumerator.TryGet(w.Handle, displays);
        if (after == null) return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Skipped, "Reason_Closed");
        if (target.WorkArea.Intersect(after.VisibleRect).Area == 0)
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_MoveRefused");

        // アプリの最小サイズなどで収まりきらなかった場合も、タイトルバー側を見える位置へ寄せる
        bool partial = !target.WorkArea.Contains(after.VisibleRect);
        if (partial)
        {
            var fixedVisible = PlacementCalculator.Clamp(after.VisibleRect, target.WorkArea);
            if (fixedVisible != after.VisibleRect)
            {
                var fw = after.Margins.ToWindowRect(fixedVisible);
                NativeMethods.SetWindowPos(w.Handle, IntPtr.Zero, fw.X, fw.Y, 0, 0, MoveFlags | NativeMethods.SWP_NOSIZE);
                after = WindowEnumerator.TryGet(w.Handle, displays) ?? after;
                partial = !target.WorkArea.Contains(after.VisibleRect);
            }
        }
        return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Moved, partial ? "Reason_DoesNotFit" : null, Partial: partial);
    }

    /// <summary>最小化・最大化中の窓は、状態を維持したまま通常表示時の位置を救出先へ変える。</summary>
    private static WindowResult MoveByPlacement(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, bool show)
    {
        var normal = w.NormalRect;
        bool normalInTarget = target.WorkArea.Contains(normal);
        var host = MonitorService.NearestToRect(displays, w.WindowRect);
        bool alreadyThere = w.State == WindowShowState.Maximized ? host?.Id == target.Id && normalInTarget : normalInTarget;
        if (alreadyThere && !show)
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Unchanged);

        var newNormal = alreadyThere ? normal
            : ComputeTarget(normal.Width, normal.Height, normal, w.Resizable, !w.IsDialog, target, o, mode, cascadeIndex, ownerVisible, out _);

        var wp = WINDOWPLACEMENT.Create();
        if (!NativeMethods.GetWindowPlacement(w.Handle, ref wp)) return Failure(w);
        var (ox, oy) = MonitorService.WorkspaceOffset(displays);
        wp.rcNormalPosition = newNormal.Offset(-ox, -oy).ToRECT();
        wp.flags = w.RestoreToMaximized ? NativeMethods.WPF_RESTORETOMAXIMIZED : 0;
        wp.showCmd = w.State switch
        {
            WindowShowState.Maximized => NativeMethods.SW_SHOWMAXIMIZED,
            _ when show => w.RestoreToMaximized ? NativeMethods.SW_SHOWMAXIMIZED : NativeMethods.SW_SHOWNORMAL,
            _ => NativeMethods.SW_SHOWMINNOACTIVE,
        };
        if (w.State == WindowShowState.Maximized && host?.Id != target.Id)
        {
            // 最大化中のままでは別画面へ移らないため、通常表示で救出先へ移してから最大化し直す
            var restore = wp;
            restore.showCmd = NativeMethods.SW_SHOWNOACTIVATE;
            if (!NativeMethods.SetWindowPlacement(w.Handle, ref restore)) return Failure(w);
        }
        if (!NativeMethods.SetWindowPlacement(w.Handle, ref wp)) return Failure(w);
        return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Moved);
    }

    private static WindowResult Failure(WindowInfo w)
    {
        int err = Marshal.GetLastWin32Error();
        string reason = err == 5 ? "Reason_AccessDenied" : "Reason_MoveRefused";
        string? detail = err != 0 ? new System.ComponentModel.Win32Exception(err).Message : null;
        return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, reason, detail);
    }
}
