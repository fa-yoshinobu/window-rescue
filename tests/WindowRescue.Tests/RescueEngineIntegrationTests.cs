using WindowRescue.Core;
using WindowRescue.Native;
using Forms = System.Windows.Forms;

namespace WindowRescue.Tests;

/// <summary>
/// 実際の Win32 窓で救出・取り消しを確認する。テスト自身が作った窓だけを操作し、利用者の窓には触れない。
/// </summary>
public class RescueEngineIntegrationTests
{
    /// <summary>別の STA スレッドでメッセージループを回すテスト用の窓。</summary>
    private sealed class TestWindow : IDisposable
    {
        private readonly Thread _thread;
        private Forms.Form? _form;
        private readonly ManualResetEventSlim _ready = new();
        public IntPtr Handle { get; private set; }

        public TestWindow(bool resizable = true)
        {
            _thread = new Thread(() =>
            {
                _form = new Forms.Form
                {
                    Text = "WindowRescue test window",
                    StartPosition = Forms.FormStartPosition.Manual,
                    Location = new System.Drawing.Point(100, 100),
                    Size = new System.Drawing.Size(640, 480),
                    ShowInTaskbar = true, // false だと WinForms が隠しオーナーを付け、ダイアログ扱いになる
                    FormBorderStyle = resizable ? Forms.FormBorderStyle.Sizable : Forms.FormBorderStyle.FixedDialog,
                    MinimumSize = new System.Drawing.Size(1, 1),
                };
                _form.Shown += (_, _) => { Handle = _form.Handle; _ready.Set(); };
                Forms.Application.Run(_form);
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)));
        }

        public void SetRect(PixelRect r) =>
            Assert.True(NativeMethods.SetWindowPos(Handle, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE));

        public void Minimize() => NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWMINNOACTIVE);

        public void Dispose()
        {
            _form?.BeginInvoke(() => _form.Close());
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private static readonly List<DisplayInfo> Displays = MonitorService.GetDisplays();

    public RescueEngineIntegrationTests()
    {
        Assert.True(DpiSetup.Applied, "test process is not per-monitor DPI aware");
        // 常駐中の Window Rescue が新しい窓（テスト用の窓）を自動補正して結果が変わるため、終了してから実行する
        Assert.True(System.Diagnostics.Process.GetProcessesByName("WindowRescue").Length == 0,
            "WindowRescue.exe が起動中です。トレイの「終了」で閉じてから結合テストを実行してください。");
    }
    private static DisplayInfo Primary => Displays.First(d => d.IsPrimary);
    private static WindowInfo Info(TestWindow w) => WindowEnumerator.TryGet(w.Handle, Displays)!;

    private static PixelRect OffscreenRect(int width, int height)
    {
        int right = Displays.Max(d => d.Bounds.Right);
        return new PixelRect(right + 500, 200, width, height);
    }

    [Fact]
    public void OffscreenWindowIsMovedIntoTarget()
    {
        using var tw = new TestWindow();
        tw.SetRect(OffscreenRect(640, 480));
        Assert.True(MonitorService.FromPoint(Displays, Info(tw).VisibleRect.CenterX, Info(tw).VisibleRect.CenterY) == null);

        var r = RescueEngine.RescueOne(Info(tw), Primary, Displays, new PlacementOptions(), false, false);

        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        Assert.True(Primary.WorkArea.Contains(Info(tw).VisibleRect), Info(tw).VisibleRect.ToString());

    }

    [Fact]
    public void TinyWindowIsEnlarged()
    {
        using var tw = new TestWindow();
        var work = Primary.WorkArea;
        var original = new PixelRect(work.X + 200, work.Y + 200, (int)(120 * Primary.Scale), (int)(60 * Primary.Scale));
        tw.SetRect(original);

        var r = RescueEngine.RescueOne(Info(tw), Primary, Displays, new PlacementOptions(), false, false);
        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        var after = Info(tw).VisibleRect;
        Assert.True(after.Width / Primary.Scale >= 160 && after.Height / Primary.Scale >= 100, after.ToString());
        Assert.True(work.Contains(after));
    }

    [Fact]
    public void FixedSizeTinyWindowIsMovedButNotEnlarged()
    {
        using var tw = new TestWindow(resizable: false);
        tw.SetRect(OffscreenRect(150, 90));
        var size = (Info(tw).WindowRect.Width, Info(tw).WindowRect.Height);
        var r = RescueEngine.RescueOne(Info(tw), Primary, Displays, new PlacementOptions(), false, false);
        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        Assert.Equal(size, (Info(tw).WindowRect.Width, Info(tw).WindowRect.Height));
        Assert.True(Primary.WorkArea.Intersect(Info(tw).VisibleRect).Area > 0);
    }

    [Fact]
    public void MinimizedWindowStaysMinimized_WithRestorePositionInTarget()
    {
        using var tw = new TestWindow();
        tw.SetRect(OffscreenRect(640, 480));
        tw.Minimize();
        Thread.Sleep(200);
        Assert.Equal(WindowShowState.Minimized, Info(tw).State);

        var r = RescueEngine.RescueOne(Info(tw), Primary, Displays, new PlacementOptions(), false, false);
        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        var now = Info(tw);
        Assert.Equal(WindowShowState.Minimized, now.State);
        Assert.True(Primary.WorkArea.Contains(now.NormalRect), now.NormalRect.ToString());
    }

    [Fact]
    public void MinimizedWindowIsShownWhenRescuedFromList()
    {
        using var tw = new TestWindow();
        tw.SetRect(OffscreenRect(640, 480));
        tw.Minimize();
        Thread.Sleep(200);

        var r = RescueEngine.RescueOne(Info(tw), Primary, Displays, new PlacementOptions(), show: true, activate: false);
        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        Thread.Sleep(200);
        var now = Info(tw);
        Assert.Equal(WindowShowState.Normal, now.State);
        Assert.True(Primary.WorkArea.Contains(now.VisibleRect), now.VisibleRect.ToString());
    }

    [Fact]
    public void MaximizedWindowMovesToOtherDisplayAndStaysMaximized()
    {
        if (Displays.Count < 2) return; // 1 画面環境では確認できない
        var source = Displays[0];
        var target = Displays[1];
        using var tw = new TestWindow();
        tw.SetRect(new PixelRect(source.WorkArea.X + 50, source.WorkArea.Y + 50, 640, 480));
        NativeMethods.ShowWindow(tw.Handle, NativeMethods.SW_SHOWMAXIMIZED);
        Thread.Sleep(300);
        Assert.Equal(WindowShowState.Maximized, Info(tw).State);

        var r = RescueEngine.RescueOne(Info(tw), target, Displays, new PlacementOptions(), false, false);
        Assert.True(r.Outcome == RescueOutcome.Moved, r.ToString());
        Thread.Sleep(300);
        var now = Info(tw);
        Assert.Equal(WindowShowState.Maximized, now.State);
        Assert.Equal(target.Id, MonitorService.NearestToRect(Displays, now.WindowRect)!.Id);
        Assert.True(target.WorkArea.Contains(now.NormalRect), now.NormalRect.ToString());
    }

    [Fact]
    public void OwnedDialogFollowsOwnerCenter()
    {
        var owner = new PixelRect(Primary.WorkArea.X + 100, Primary.WorkArea.Y + 100, 800, 600);
        var placed = PlacementCalculator.CenterOver(300, 200, owner, Primary.WorkArea);
        Assert.Equal(owner.CenterX, placed.CenterX);
        Assert.Equal(owner.CenterY, placed.CenterY);
    }
}
