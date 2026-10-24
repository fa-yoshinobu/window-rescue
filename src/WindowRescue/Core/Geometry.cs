using WindowRescue.Native;

namespace WindowRescue.Core;

/// <summary>物理ピクセル座標の矩形（仮想スクリーン座標）。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);
    internal static PixelRect From(RECT r) => FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    internal RECT ToRECT() => new(X, Y, Right, Bottom);

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    public bool Contains(PixelRect r) => r.X >= X && r.Y >= Y && r.Right <= Right && r.Bottom <= Bottom;

    public PixelRect Intersect(PixelRect o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? FromLTRB(l, t, r, b) : default;
    }

    public long Area => IsEmpty ? 0 : (long)Width * Height;
    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public override string ToString() => $"({X}, {Y}) {Width}×{Height}";
}

/// <summary>ウインドウ矩形と、見えている枠（DWM の拡張フレーム）との差分。</summary>
public readonly record struct FrameMargins(int Left, int Top, int Right, int Bottom)
{
    public static FrameMargins Between(PixelRect window, PixelRect visible) =>
        new(visible.X - window.X, visible.Y - window.Y, window.Right - visible.Right, window.Bottom - visible.Bottom);

    public PixelRect ToWindowRect(PixelRect visible) =>
        PixelRect.FromLTRB(visible.X - Left, visible.Y - Top, visible.Right + Right, visible.Bottom + Bottom);
}
