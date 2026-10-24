using WindowRescue.Settings;

namespace WindowRescue.Core;

/// <summary>接続中の画面と、画面番号・救出先の設定を突き合わせる（第9章）。接続中の画面はすべて使用する。</summary>
public sealed class DisplayManager
{
    private readonly AppSettings _settings;

    public DisplayManager(AppSettings settings) => _settings = settings;

    public IReadOnlyList<DisplayInfo> Connected { get; private set; } = [];

    /// <summary>画面情報を取得し直し、設定を接続構成に合わせる。設定が変わった場合 true。</summary>
    public bool Refresh(IReadOnlyList<DisplayInfo> displays, (int x, int y) cursor)
    {
        bool changed = false;
        Connected = displays;

        // 新しい画面に番号を割り当てる（左→右、上→下の順）
        foreach (var d in displays.OrderBy(d => d.Bounds.X).ThenBy(d => d.Bounds.Y))
        {
            var known = _settings.Displays.FirstOrDefault(k => k.Id == d.Id);
            if (known == null)
            {
                known = new KnownDisplay { Id = d.Id, Number = NextFreeNumber() };
                _settings.Displays.Add(known);
                changed = true;
            }
            if (known.Name != d.FriendlyName) { known.Name = d.FriendlyName; changed = true; }
        }

        // 救出先が切断された場合は、ポインターのある画面を代わりに使う
        if (ById(_settings.DestinationId) == null && displays.Count > 0)
        {
            _settings.DestinationId = PickFallbackDestination(cursor)?.Id;
            changed = true;
        }
        return changed;
    }

    private int NextFreeNumber()
    {
        int n = 1;
        var used = _settings.Displays.Select(k => k.Number).ToHashSet();
        while (used.Contains(n)) n++;
        return n;
    }

    public int NumberOf(DisplayInfo d) => _settings.Displays.FirstOrDefault(k => k.Id == d.Id)?.Number ?? 0;

    public DisplayInfo? ByNumber(int n)
    {
        var known = _settings.Displays.FirstOrDefault(k => k.Number == n);
        return known == null ? null : Connected.FirstOrDefault(d => d.Id == known.Id);
    }

    public DisplayInfo? ById(string? id) => id == null ? null : Connected.FirstOrDefault(d => d.Id == id);

    /// <summary>救出先。指定の画面が接続されていなければ、ポインターのある画面を使う。</summary>
    public DisplayInfo? GetDestination((int x, int y) cursor) =>
        ById(_settings.DestinationId) ?? PickFallbackDestination(cursor);

    private DisplayInfo? PickFallbackDestination((int x, int y) cursor) =>
        Connected.FirstOrDefault(a => a.Bounds.Contains(cursor.x, cursor.y))
        ?? Connected.FirstOrDefault(a => a.IsPrimary)
        ?? Connected.FirstOrDefault();

    public bool SetDestination(string id)
    {
        if (ById(id) == null) return false;
        _settings.DestinationId = id;
        return true;
    }
}
