using WindowRescue.Core;

namespace WindowRescue.Tests;

public class PlacementTests
{
    // 3 画面：左 100%（プライマリ）、中央 150%、右 200%（負の座標・タスクバーを含む）
    private static readonly DisplayInfo D1 = new("d1", @"\\.\DISPLAY1", "A", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032), 96, true);
    private static readonly DisplayInfo D2 = new("d2", @"\\.\DISPLAY2", "B", new PixelRect(1920, 0, 2560, 1440), new PixelRect(1920, 0, 2560, 1380), 144, false);
    private static readonly DisplayInfo D3 = new("d3", @"\\.\DISPLAY3", "C", new PixelRect(-3840, 0, 3840, 2160), new PixelRect(-3840, 0, 3840, 2064), 192, false);
    private static readonly DisplayInfo[] All = [D1, D2, D3];
    private static readonly PlacementOptions O = new();

    [Fact]
    public void Clamp_MovesMinimallyIntoWorkArea()
    {
        var r = PlacementCalculator.Clamp(new PixelRect(1800, 900, 400, 300), D1.WorkArea);
        Assert.Equal(new PixelRect(1520, 732, 400, 300), r);
    }

    [Fact]
    public void Clamp_TooLargeKeepsTitleBarVisible()
    {
        var r = PlacementCalculator.Clamp(new PixelRect(-500, -200, 3000, 2000), D1.WorkArea);
        Assert.Equal(D1.WorkArea.X, r.X);
        Assert.Equal(D1.WorkArea.Y, r.Y);
    }

    [Fact]
    public void TargetSize_TinyResizableIsEnlargedInDip()
    {
        // 200% の画面で 100×60 px（= 50×30 DIP）は極小 → 1000×700 DIP = 2000×1400 px
        var (w, h, resized) = PlacementCalculator.TargetSize(100, 60, D3.Scale, D3.WorkArea, true, true, O);
        Assert.True(resized);
        Assert.Equal(2000, w);
        Assert.Equal(1400, h);
    }

    [Fact]
    public void TargetSize_NotTinyAt200PercentIsKept()
    {
        // 物理 300×220 px は 200% では 150×110 DIP → 幅 160 未満なので極小。320×220 px は 160×110 DIP → 正常
        var (w, h, resized) = PlacementCalculator.TargetSize(320, 220, D3.Scale, D3.WorkArea, true, true, O);
        Assert.False(resized);
        Assert.Equal((320, 220), (w, h));
    }

    [Fact]
    public void TargetSize_StandardSizeLimitedTo90PercentOfWorkArea()
    {
        var small = new PixelRect(0, 0, 800, 600);
        var (w, h, _) = PlacementCalculator.TargetSize(10, 10, 1.0, small, true, true, O);
        Assert.Equal(720, w);
        Assert.Equal(540, h);
    }

    [Fact]
    public void TargetSize_FixedSizeWindowIsNotResized()
    {
        var (w, h, resized) = PlacementCalculator.TargetSize(50, 20, 1.0, D1.WorkArea, false, true, O);
        Assert.False(resized);
        Assert.Equal((50, 20), (w, h));
    }

    [Fact]
    public void TargetSize_TinyFixDisabledKeepsSize()
    {
        var (w, h, resized) = PlacementCalculator.TargetSize(50, 20, 1.0, D1.WorkArea, true, true, O with { FixTinySize = false });
        Assert.False(resized);
        Assert.Equal((50, 20), (w, h));
    }

    [Fact]
    public void Cascade_OffsetsTitleBarsAndStaysInside()
    {
        var a = PlacementCalculator.Cascade(800, 600, D1.WorkArea, 0, 32);
        var b = PlacementCalculator.Cascade(800, 600, D1.WorkArea, 1, 32);
        Assert.Equal(32, b.X - a.X);
        Assert.Equal(32, b.Y - a.Y);
        for (int i = 0; i < 40; i++)
            Assert.True(D1.WorkArea.Contains(PlacementCalculator.Cascade(800, 600, D1.WorkArea, i, 32)));
    }

    [Fact]
    public void Judge_WindowOnUnusedDisplayMovesToDestination()
    {
        var active = new[] { D2, D3 };
        var (v, _) = WindowJudge.Judge(new PixelRect(100, 100, 800, 600), WindowShowState.Normal, true, false, null, active, All, O);
        Assert.Equal(Verdict.MoveToDestination, v);
    }

    [Fact]
    public void Judge_OffscreenWindowMovesToDestination()
    {
        var (v, _) = WindowJudge.Judge(new PixelRect(10000, 5000, 800, 600), WindowShowState.Normal, true, false, null, All, All, O);
        Assert.Equal(Verdict.MoveToDestination, v);
    }

    [Fact]
    public void Judge_NormalWindowIsKept()
    {
        var (v, _) = WindowJudge.Judge(new PixelRect(100, 100, 800, 600), WindowShowState.Normal, true, false, null, All, All, O);
        Assert.Equal(Verdict.Ok, v);
    }

    [Fact]
    public void Judge_WindowSpanningUsedDisplaysIsKept()
    {
        // 画面 1 と画面 2 にまたがるが、両方使用中でタイトルバーも操作できる
        var (v, _) = WindowJudge.Judge(new PixelRect(1500, 100, 800, 600), WindowShowState.Normal, true, false, null, All, All, O);
        Assert.Equal(Verdict.Ok, v);
    }

    [Fact]
    public void Judge_TitleBarAboveScreenIsFitted()
    {
        var (v, d) = WindowJudge.Judge(new PixelRect(100, -300, 800, 600), WindowShowState.Normal, true, false, null, All, All, O);
        Assert.Equal(Verdict.FitInDisplay, v);
        Assert.Equal("d1", d?.Id);
    }

    [Fact]
    public void Judge_TinyResizableWindowIsEnlarged()
    {
        var (v, _) = WindowJudge.Judge(new PixelRect(100, 100, 120, 60), WindowShowState.Normal, true, false, null, All, All, O);
        Assert.Equal(Verdict.EnlargeInDisplay, v);
    }

    [Fact]
    public void Judge_SmallDialogIsNotEnlarged()
    {
        var (v, _) = WindowJudge.Judge(new PixelRect(100, 100, 120, 60), WindowShowState.Normal, true, true, null, All, All, O);
        Assert.Equal(Verdict.Ok, v);
    }

    [Fact]
    public void Judge_DialogWhoseOwnerIsOnUnusedDisplayMovesToDestination()
    {
        var active = new[] { D2 };
        var (v, _) = WindowJudge.Judge(new PixelRect(2000, 100, 400, 300), WindowShowState.Normal, false, true,
            new PixelRect(100, 100, 800, 600), active, All, O);
        Assert.Equal(Verdict.MoveToDestination, v);
    }

    [Fact]
    public void Judge_MaximizedOnUnusedDisplayMoves()
    {
        var active = new[] { D2 };
        var (v, _) = WindowJudge.Judge(new PixelRect(-8, -8, 1936, 1048), WindowShowState.Maximized, true, false, null, active, All, O);
        Assert.Equal(Verdict.MoveToDestination, v);
    }

    [Fact]
    public void Margins_RoundTrip()
    {
        var window = new PixelRect(93, 100, 814, 607);
        var visible = new PixelRect(100, 100, 800, 600);
        var m = FrameMargins.Between(window, visible);
        Assert.Equal(window, m.ToWindowRect(visible));
    }
}
