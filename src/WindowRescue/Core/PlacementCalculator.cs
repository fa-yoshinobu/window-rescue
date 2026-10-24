namespace WindowRescue.Core;

/// <summary>第8章の数値（DIP）。</summary>
public sealed record PlacementOptions
{
    public int TinyWidthDip { get; init; } = 160;
    public int TinyHeightDip { get; init; } = 100;
    public int StandardWidthDip { get; init; } = 1000;
    public int StandardHeightDip { get; init; } = 700;
    public double StandardMaxRatio { get; init; } = 0.9;
    public int CascadeStepDip { get; init; } = 32;
    /// <summary>極小窓の自動サイズ補正を行うか。</summary>
    public bool FixTinySize { get; init; } = true;
}

/// <summary>位置・サイズ計算（Win32 に依存しない純粋な計算）。すべて物理ピクセルの「見えている枠」で扱う。</summary>
public static class PlacementCalculator
{
    public static bool IsTiny(PixelRect visible, double scale, PlacementOptions o) =>
        visible.Width / scale < o.TinyWidthDip || visible.Height / scale < o.TinyHeightDip;

    /// <summary>救出先での大きさ。サイズ変更可能な窓だけ、極小・救出先より大きい場合に補正する。</summary>
    public static (int Width, int Height, bool Resized) TargetSize(
        int width, int height, double targetScale, PixelRect work, bool resizable, bool allowTinyFix, PlacementOptions o)
    {
        if (!resizable) return (width, height, false);

        int maxW = (int)(work.Width * o.StandardMaxRatio);
        int maxH = (int)(work.Height * o.StandardMaxRatio);

        if (allowTinyFix && o.FixTinySize &&
            (width / targetScale < o.TinyWidthDip || height / targetScale < o.TinyHeightDip))
        {
            int w = Math.Min((int)Math.Round(o.StandardWidthDip * targetScale), maxW);
            int h = Math.Min((int)Math.Round(o.StandardHeightDip * targetScale), maxH);
            // 片方だけ小さい場合も、大きい方の辺は維持する
            return (Math.Max(w, Math.Min(width, maxW)), Math.Max(h, Math.Min(height, maxH)), true);
        }

        bool resized = false;
        if (width > work.Width) { width = maxW; resized = true; }
        if (height > work.Height) { height = maxH; resized = true; }
        return (width, height, resized);
    }

    /// <summary>
    /// 作業領域へ必要最小限の移動で収める。収まらない大きさの場合は左上（タイトルバー側）を優先して見せる。
    /// </summary>
    public static PixelRect Clamp(PixelRect r, PixelRect work)
    {
        int x = r.Width >= work.Width ? work.X : Math.Clamp(r.X, work.X, work.Right - r.Width);
        int y = r.Height >= work.Height ? work.Y : Math.Clamp(r.Y, work.Y, work.Bottom - r.Height);
        return r with { X = x, Y = y };
    }

    public static PixelRect Center(int width, int height, PixelRect work) =>
        Clamp(new PixelRect(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height), work);

    /// <summary>一括救出時の段違い配置。index 番目の窓を step ずつずらし、はみ出す前に先頭へ戻す。</summary>
    public static PixelRect Cascade(int width, int height, PixelRect work, int index, int stepPx)
    {
        stepPx = Math.Max(1, stepPx);
        int slotsX = Math.Max(1, (work.Width - width) / stepPx + 1);
        int slotsY = Math.Max(1, (work.Height - height) / stepPx + 1);
        int slots = Math.Max(1, Math.Min(Math.Min(slotsX, slotsY), 12));
        int n = index % slots;
        int lap = index / slots;
        // 一周するごとに横へ少しずらし、タイトルが完全に重ならないようにする
        int lapShift = (lap * stepPx * 3) % Math.Max(1, work.Width - width - n * stepPx + 1);
        var r = new PixelRect(work.X + n * stepPx + lapShift, work.Y + n * stepPx, width, height);
        return Clamp(r, work);
    }

    /// <summary>親ウインドウの中央に重ね、作業領域へ収める。</summary>
    public static PixelRect CenterOver(int width, int height, PixelRect owner, PixelRect work) =>
        Clamp(new PixelRect(owner.CenterX - width / 2, owner.CenterY - height / 2, width, height), work);
}

public enum Verdict
{
    Ok,
    /// <summary>救出先へ移す。</summary>
    MoveToDestination,
    /// <summary>所在する使用中画面の作業領域へ収める。</summary>
    FitInDisplay,
    /// <summary>所在する使用中画面で標準救出サイズへ拡大する。</summary>
    EnlargeInDisplay,
}

/// <summary>第7.3節の判定。</summary>
public static class WindowJudge
{
    public static (Verdict Verdict, DisplayInfo? Display) Judge(
        PixelRect visible,
        WindowShowState state,
        bool resizable,
        bool isDialog,
        PixelRect? ownerVisible,
        IReadOnlyList<DisplayInfo> active,
        IReadOnlyList<DisplayInfo> all,
        PlacementOptions o)
    {
        if (active.Count == 0) return (Verdict.Ok, null);

        if (state == WindowShowState.Maximized)
        {
            var host = MonitorService.NearestToRect(all, visible);
            return host != null && active.Any(a => a.Id == host.Id) ? (Verdict.Ok, host) : (Verdict.MoveToDestination, null);
        }

        // 使用中の画面とまったく重ならない
        if (!active.Any(a => a.Bounds.Intersect(visible).Area > 0)) return (Verdict.MoveToDestination, null);

        // 使用しない画面に中心がある
        var centerDisplay = MonitorService.FromPoint(all, visible.CenterX, visible.CenterY);
        if (centerDisplay != null && !active.Any(a => a.Id == centerDisplay.Id)) return (Verdict.MoveToDestination, null);

        DisplayInfo home = active.OrderByDescending(a => a.Bounds.Intersect(visible).Area).First();

        // ダイアログは親のある使用中画面に収める。親が使用中画面になければ救出先へ
        if (isDialog && ownerVisible is { } ov)
        {
            var ownerDisplay = MonitorService.NearestToRect(all, ov);
            if (ownerDisplay == null || !active.Any(a => a.Id == ownerDisplay.Id)) return (Verdict.MoveToDestination, null);
            home = active.First(a => a.Id == ownerDisplay.Id);
        }

        if (!IsOperable(visible, active, home.Scale)) return (Verdict.FitInDisplay, home);

        if (resizable && !isDialog && o.FixTinySize && PlacementCalculator.IsTiny(visible, home.Scale, o))
            return (Verdict.EnlargeInDisplay, home);

        return (Verdict.Ok, home);
    }

    /// <summary>タイトルバーが操作でき、窓全体が使用中画面群の作業領域に収まっているか。</summary>
    public static bool IsOperable(PixelRect visible, IReadOnlyList<DisplayInfo> active, double scale)
    {
        bool InWork(int x, int y) => active.Any(a => a.WorkArea.Contains(x, y));
        int titleY = visible.Y + (int)Math.Round(Math.Min(12 * scale, Math.Max(1, visible.Height / 2)));
        if (!InWork(visible.CenterX, titleY)) return false;
        return InWork(visible.X, visible.Y) && InWork(visible.Right - 1, visible.Y)
            && InWork(visible.X, visible.Bottom - 1) && InWork(visible.Right - 1, visible.Bottom - 1);
    }
}
