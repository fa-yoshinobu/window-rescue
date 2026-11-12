using System.Windows.Threading;
using WindowRescue.Native;

namespace WindowRescue.Core;

/// <summary>
/// 新規ウインドウの監視（第7章）。WinEvent で表示を受け取り、表示が落ち着いてから判定する。
/// 最初の補正後に位置を戻された場合は、出現から 2 秒以内に 1 回だけ追加補正する。
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RecheckDelay = TimeSpan.FromMilliseconds(1500);
    private const int MaxCorrections = 2;

    private sealed class Tracked
    {
        public required IntPtr Handle;
        public required DateTime FirstSeen;
        public int Corrections;
        public bool UserMoved;
        public PixelRect? CorrectedRect;
    }

    private readonly Dispatcher _dispatcher;
    private readonly Func<IntPtr, bool, Task<PixelRect?>> _check; // (hwnd, isRecheck) → 補正後の矩形（補正しなければ null）
    private readonly NativeMethods.WinEventDelegate _callback; // GC されないよう保持
    private readonly Dictionary<IntPtr, Tracked> _tracked = [];
    private IntPtr _showHook, _moveHook, _uncloakHook;
    private WindowAppearances? _appearances;

    public WindowWatcher(Dispatcher dispatcher, Func<IntPtr, bool, Task<PixelRect?>> check)
    {
        _dispatcher = dispatcher;
        _check = check;
        _callback = OnWinEvent;
    }

    public bool IsRunning => _showHook != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning) return;
        var existing = new List<IntPtr>();
        NativeMethods.EnumWindows((hwnd, _) => { existing.Add(hwnd); return true; }, IntPtr.Zero);
        _appearances = new WindowAppearances(existing);
        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _showHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_DESTROY, NativeMethods.EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, flags);
        _moveHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZESTART, IntPtr.Zero, _callback, 0, 0, flags);
        _uncloakHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_UNCLOAKED, NativeMethods.EVENT_OBJECT_UNCLOAKED, IntPtr.Zero, _callback, 0, 0, flags);
    }

    public void Stop()
    {
        if (_showHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_showHook);
        if (_moveHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_moveHook);
        if (_uncloakHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_uncloakHook);
        _showHook = _moveHook = _uncloakHook = IntPtr.Zero;
        _tracked.Clear();
        _appearances = null;
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (_appearances == null || idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF || hwnd == IntPtr.Zero) return;

        if (evt == NativeMethods.EVENT_OBJECT_DESTROY)
        {
            _appearances.Forget(hwnd);
            _tracked.Remove(hwnd);
            return;
        }

        if (evt == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
        {
            // 補正待ちの間に利用者が動かした窓は、その出現に対する自動補正を中止する
            if (_tracked.TryGetValue(hwnd, out var t)) t.UserMoved = true;
            return;
        }

        if (evt == NativeMethods.EVENT_OBJECT_SHOW)
        {
            if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return;
            if (!_appearances.Show(hwnd, NativeMethods.IsCloaked(hwnd)) || !WindowEnumerator.IsTarget(hwnd)) return;
        }
        else if (evt == NativeMethods.EVENT_OBJECT_UNCLOAKED)
        {
            if (!_appearances.Uncloak(hwnd, WindowEnumerator.IsTarget(hwnd))) return;
        }
        else return;

        // SHOW と UNCLOAKED が重なっても同じ出現の補正回数をリセットしない。
        if (_tracked.ContainsKey(hwnd)) return;
        var tracked = new Tracked { Handle = hwnd, FirstSeen = DateTime.UtcNow };
        _tracked[hwnd] = tracked;
        Schedule(tracked, FirstCheckDelay, recheck: false);
    }

    private void Schedule(Tracked t, TimeSpan delay, bool recheck)
    {
        var timer = new DispatcherTimer(delay, DispatcherPriority.Background, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            Evaluate(t, recheck);
        }, _dispatcher);
        timer.Start();
    }

    private async void Evaluate(Tracked t, bool recheck)
    {
        if (!_tracked.TryGetValue(t.Handle, out var current) || !ReferenceEquals(current, t)) return;
        if (t.UserMoved || !NativeMethods.IsWindow(t.Handle) || t.Corrections >= MaxCorrections)
        {
            _tracked.Remove(t.Handle);
            return;
        }

        PixelRect? corrected;
        try { corrected = await _check(t.Handle, recheck); }
        catch (Exception ex) { Log.Error("Automatic window correction", ex); corrected = null; }
        if (!_tracked.TryGetValue(t.Handle, out current) || !ReferenceEquals(current, t)) return;
        if (t.UserMoved) { _tracked.Remove(t.Handle); return; }
        if (corrected != null)
        {
            t.Corrections++;
            t.CorrectedRect = corrected;
        }

        // 最初の補正のあとだけ、出現から 2 秒以内に位置が戻されていないか確認する
        if (!recheck && t.Corrections > 0 && t.Corrections < MaxCorrections)
        {
            var elapsed = DateTime.UtcNow - t.FirstSeen;
            var remaining = TimeSpan.FromSeconds(2) - elapsed;
            Schedule(t, remaining > TimeSpan.FromMilliseconds(100) ? (remaining < RecheckDelay ? remaining : RecheckDelay) : TimeSpan.FromMilliseconds(100), recheck: true);
        }
        else
        {
            _tracked.Remove(t.Handle);
        }
    }

    public void Dispose() => Stop();
}

/// <summary>監視開始後に初めて SHOW された窓だけ、クローク解除を待つ。</summary>
internal sealed class WindowAppearances(IEnumerable<IntPtr> existing)
{
    private readonly HashSet<IntPtr> _known = new(existing);
    private readonly HashSet<IntPtr> _waiting = [];

    public bool Show(IntPtr hwnd, bool cloaked)
    {
        bool first = _known.Add(hwnd);
        if (cloaked)
        {
            // 既存の窓のクローク解除（仮想デスクトップ切替など）は新規出現にしない。
            if (first) _waiting.Add(hwnd);
            return false;
        }
        _waiting.Remove(hwnd);
        return true;
    }

    public bool Uncloak(IntPtr hwnd, bool eligible) => eligible && _waiting.Remove(hwnd);

    public void Forget(IntPtr hwnd)
    {
        _known.Remove(hwnd);
        _waiting.Remove(hwnd);
    }
}
