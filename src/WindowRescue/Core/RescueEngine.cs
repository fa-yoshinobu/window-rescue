using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using WindowRescue.Native;

namespace WindowRescue.Core;

public enum RescueOutcome { Moved, Unchanged, Skipped, Failed, Pending }

/// <summary>1 窓の処理結果。Reason はリソースキー、Detail は OS 由来の原文。</summary>
public sealed record WindowResult(IntPtr Handle, string Label, RescueOutcome Outcome, string? Reason = null, string? Detail = null, bool Partial = false);

public sealed class RescueReport
{
    public List<WindowResult> Results { get; } = [];
    public int Moved => Results.Count(r => r.Outcome is RescueOutcome.Moved or RescueOutcome.Unchanged);
    public int Skipped => Results.Count(r => r.Outcome == RescueOutcome.Skipped);
    public int Failed => Results.Count(r => r.Outcome == RescueOutcome.Failed);
    public int Pending => Results.Count(r => r.Outcome == RescueOutcome.Pending);
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
    private const uint MoveFlags = NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_ASYNCWINDOWPOS;
    private static readonly ConcurrentDictionary<IntPtr, (uint Process, long Start, WindowMove Move)> PendingMoves = new();

    /// <summary>F-02：全対象ウインドウを target へ集める。</summary>
    public static RescueReport RescueAll(DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        Func<WindowInfo, bool> isExcluded, CancellationToken ct) =>
        RescueAll(target, displays, o, isExcluded, ct, WindowEnumerator.Enumerate(displays));

    internal static RescueReport RescueAll(DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        Func<WindowInfo, bool> isExcluded, CancellationToken ct, List<WindowInfo> windows)
    {
        var report = new RescueReport();
        IntPtr foreground = NativeMethods.GetForegroundWindow();
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
            var result = Execute(w, move => Move(w, target, displays, o, PlaceMode.Cascade, cascade, owner, false, move));
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
        var r = Execute(w, move => Move(w, target, displays, o, PlaceMode.KeepOrCenter, 0, owner, show, move));
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
                Execute(w, move => Move(w, home, displays, opts, PlaceMode.KeepOrCenter, 0, null, false, move)),
            _ => new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Unchanged),
        };
    }

    private static WindowResult Execute(WindowInfo w, Func<WindowMove, WindowResult> action)
    {
        foreach (var handle in PendingMoves.Keys)
            if (!NativeMethods.IsWindow(handle)) PendingMoves.TryRemove(handle, out _);
        if (PendingMoves.TryGetValue(w.Handle, out var pending))
        {
            // 先の要求が残っている窓への再試行も「失敗」にしない。確認できるまで追加要求を送らない。
            if (pending.Process == w.ProcessId && pending.Start == w.ProcessStartTime && !pending.Move.IsApplied())
                return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Pending, "Reason_MovePending");
            PendingMoves.TryRemove(w.Handle, out _);
        }
        var move = new WindowMove(w.Handle);
        WindowResult result;
        try
        {
            // 応答確認は移動要求を出す前に行う。失敗した窓には何もキューへ積まない。
            result = !NativeMethods.IsWindow(w.Handle)
                ? new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Skipped, "Reason_Closed")
                : !WindowMove.Responds(w.Handle)
                ? new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_NotResponding")
                : action(move);
        }
        catch (MovePendingException)
        {
            result = new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Pending, "Reason_MovePending");
        }
        catch (Exception ex)
        {
            Log.Error("Rescue exception", ex);
            result = new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Failed, "Reason_Unexpected", ex.Message);
        }
        // 非同期要求は取消不能。送信後に応答が途絶えた場合は「失敗後に遅れて移動」と誤報しない。
        if (move.Submitted && result.Outcome == RescueOutcome.Failed)
            result = result with { Outcome = RescueOutcome.Pending, Reason = "Reason_MovePending" };
        if (result.Outcome == RescueOutcome.Pending)
            PendingMoves[w.Handle] = (w.ProcessId, w.ProcessStartTime, move);
        Log.RescueFailed(w, result);
        return result;
    }

    private sealed class MovePendingException : Exception;

    private sealed class WindowMove(IntPtr hwnd)
    {
        public bool Submitted { get; private set; }
        private readonly System.Diagnostics.Stopwatch _elapsed = System.Diagnostics.Stopwatch.StartNew();
        private Func<bool>? _applied;

        public bool IsApplied() => _applied?.Invoke() == true && Responds(hwnd);

        // WM_NULL、SMTO_BLOCK | SMTO_ABORTIFHUNG。移動処理用のタスクを放置せず、OS側で待ち時間を制限する。
        public static bool Responds(IntPtr handle) =>
            NativeMethods.SendMessageTimeout(handle, 0, IntPtr.Zero, IntPtr.Zero, 3, 50, out _) != IntPtr.Zero;

        public bool Submit(Func<bool> request, Func<bool> applied)
        {
            if (Submitted && !Responds(hwnd)) throw new MovePendingException();
            if (!request()) return false;
            Submitted = true;
            _applied = applied;
            // ASYNCWINDOWPOS / ASYNCWINDOWPLACEMENT は応答停止でも呼出し元を拘束しない。
            // 状態確認にも上限を設ける。未反映なら確認待ちとして終了し、裏で再試行しない。
            while (NativeMethods.IsWindow(hwnd))
            {
                if (IsApplied()) return true;
                if (_elapsed.ElapsedMilliseconds >= 2000) throw new MovePendingException();
                Thread.Sleep(5);
            }
            return true;
        }

        public bool Position(PixelRect rect, uint flags)
        {
            NativeMethods.GetWindowRect(hwnd, out var before);
            return Submit(() => NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, rect.X, rect.Y, rect.Width, rect.Height, flags),
                () => NativeMethods.GetWindowRect(hwnd, out var after)
                    && (!after.Equals(before) || (after.Left == rect.X && after.Top == rect.Y
                        && ((flags & NativeMethods.SWP_NOSIZE) != 0 || after.Right - after.Left == rect.Width && after.Bottom - after.Top == rect.Height))));
        }

        public bool Placement(WINDOWPLACEMENT placement)
        {
            placement.flags |= NativeMethods.WPF_ASYNCWINDOWPLACEMENT;
            return Submit(() => NativeMethods.SetWindowPlacement(hwnd, ref placement), () =>
            {
                var current = WINDOWPLACEMENT.Create();
                return NativeMethods.GetWindowPlacement(hwnd, ref current)
                    && current.rcNormalPosition.Equals(placement.rcNormalPosition)
                    && (placement.showCmd == NativeMethods.SW_SHOWMINNOACTIVE ? NativeMethods.IsIconic(hwnd)
                        : placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED ? NativeMethods.IsZoomed(hwnd)
                        : !NativeMethods.IsIconic(hwnd) && !NativeMethods.IsZoomed(hwnd));
            });
        }
    }

    private static WindowResult Move(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, bool show, WindowMove move)
    {
        if (!NativeMethods.IsWindow(w.Handle))
            return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Skipped, "Reason_Closed");

        WindowResult result = w.State switch
        {
            WindowShowState.Normal => MoveNormal(w, target, displays, o, mode, cascadeIndex, ownerVisible, move),
            _ => MoveByPlacement(w, target, displays, o, mode, cascadeIndex, ownerVisible, show, move),
        };

        // 個別救出：最小化中の窓は通常表示へ戻す（上で位置は変更済み）
        if (show && result.Outcome != RescueOutcome.Failed && NativeMethods.IsIconic(w.Handle))
            if (!move.Submit(() => NativeMethods.ShowWindowAsync(w.Handle, NativeMethods.SW_RESTORE), () => !NativeMethods.IsIconic(w.Handle)))
                return Failure(w);

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
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, WindowMove move)
    {
        var info = w;
        var source = MonitorService.NearestToRect(displays, w.VisibleRect);

        // 拡大率が異なる画面へは、先に位置だけ移してアプリ自身の DPI 変更処理を済ませる
        if (source != null && source.Dpi != target.Dpi && target.WorkArea.Intersect(w.VisibleRect).Area == 0)
        {
            var pre = PlacementCalculator.Center(Math.Min(w.WindowRect.Width, target.WorkArea.Width),
                Math.Min(w.WindowRect.Height, target.WorkArea.Height), target.WorkArea);
            if (!move.Position(pre, MoveFlags | NativeMethods.SWP_NOSIZE))
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
        if (!move.Position(newWindow, flags))
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
                if (!move.Position(fw, MoveFlags | NativeMethods.SWP_NOSIZE)) return Failure(w);
                after = WindowEnumerator.TryGet(w.Handle, displays) ?? after;
                partial = !target.WorkArea.Contains(after.VisibleRect);
            }
        }
        return new WindowResult(w.Handle, w.DisplayLabel, RescueOutcome.Moved, partial ? "Reason_DoesNotFit" : null, Partial: partial);
    }

    /// <summary>最小化・最大化中の窓は、状態を維持したまま通常表示時の位置を救出先へ変える。</summary>
    private static WindowResult MoveByPlacement(WindowInfo w, DisplayInfo target, IReadOnlyList<DisplayInfo> displays, PlacementOptions o,
        PlaceMode mode, int cascadeIndex, PixelRect? ownerVisible, bool show, WindowMove move)
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
            if (!move.Placement(restore)) return Failure(w);
        }
        if (!move.Placement(wp)) return Failure(w);
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
