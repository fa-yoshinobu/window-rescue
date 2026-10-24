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
    private IntPtr _showHook, _moveHook;

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
        uint flags = NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS;
        _showHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, flags);
        _moveHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MOVESIZESTART, NativeMethods.EVENT_SYSTEM_MOVESIZESTART, IntPtr.Zero, _callback, 0, 0, flags);
    }

    public void Stop()
    {
        if (_showHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_showHook);
        if (_moveHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_moveHook);
        _showHook = _moveHook = IntPtr.Zero;
        _tracked.Clear();
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF || hwnd == IntPtr.Zero) return;

        if (evt == NativeMethods.EVENT_SYSTEM_MOVESIZESTART)
        {
            // 補正待ちの間に利用者が動かした窓は、その出現に対する自動補正を中止する
            if (_tracked.TryGetValue(hwnd, out var t)) t.UserMoved = true;
            return;
        }

        if (evt == NativeMethods.EVENT_OBJECT_SHOW)
        {
            if (!WindowEnumerator.IsTarget(hwnd)) return;
            // 非表示から再表示された場合も、新しい出現として扱う
            var tracked = new Tracked { Handle = hwnd, FirstSeen = DateTime.UtcNow };
            _tracked[hwnd] = tracked;
            Schedule(tracked, FirstCheckDelay, recheck: false);
        }
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
        catch (Exception) { corrected = null; }
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
