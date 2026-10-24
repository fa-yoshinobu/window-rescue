using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowRescue.Core;
using WindowRescue.Localization;

namespace WindowRescue.UI;

/// <summary>一覧の 1 行。外部アプリの名前・タイトルは原文のまま表示する。</summary>
public sealed class WindowRow
{
    public required WindowInfo Info { get; init; }
    public string AppName => Info.AppName;
    public string ProcessName => Info.ProcessName;
    public string Title => string.IsNullOrWhiteSpace(Info.Title) ? Loc.T("List_NoTitle") : Info.Title;
    public required string Location { get; init; }
    public string Position => $"{Info.VisibleRect.X}, {Info.VisibleRect.Y}";
    public string Size => $"{Info.VisibleRect.Width} × {Info.VisibleRect.Height}";
    public string State => Loc.T(Info.State switch
    {
        WindowShowState.Minimized => "State_Minimized",
        WindowShowState.Maximized => "State_Maximized",
        _ => "State_Normal",
    });
    public required List<string> IssueList { get; init; }
    public string Issues => string.Join(Loc.T("List_IssueSeparator"), IssueList);
    public bool HasIssue => IssueList.Count > 0;
    public Visibility IssueIconVisibility => HasIssue ? Visibility.Visible : Visibility.Collapsed;
}

public partial class WindowListWindow : Window
{
    private readonly AppController _app;
    private List<WindowRow> _rows = [];
    private readonly List<WindowRow> _selectionOrder = [];

    public WindowListWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.StateChanged += RefreshDestinations;
        Loc.Instance.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => { _app.StateChanged -= RefreshDestinations; Loc.Instance.LanguageChanged -= OnLanguageChanged; };
    }

    private void OnLanguageChanged()
    {
        // 選択や検索条件を保ったまま、表示文字列だけを作り直す
        var selected = Grid.SelectedItems.Cast<WindowRow>().Select(r => r.Info.Handle).ToHashSet();
        Reload();
        foreach (var row in Grid.Items.Cast<WindowRow>().Where(r => selected.Contains(r.Info.Handle))) Grid.SelectedItems.Add(row);
    }

    public void Reload()
    {
        RefreshDestinations();
        var displays = _app.Displays.Connected;
        var active = displays;
        var options = _app.Settings.ToPlacementOptions();
        _rows = WindowEnumerator.Enumerate(displays).Select(w => BuildRow(w, displays, active, options)).ToList();
        _selectionOrder.Clear();
        ApplyFilter();
    }

    private WindowRow BuildRow(WindowInfo w, IReadOnlyList<DisplayInfo> displays, IReadOnlyList<DisplayInfo> active, PlacementOptions o)
    {
        var issues = new List<string>();
        var v = w.VisibleRect;
        var host = w.State == WindowShowState.Maximized
            ? MonitorService.NearestToRect(displays, w.WindowRect)
            : MonitorService.FromPoint(displays, v.CenterX, v.CenterY)
              ?? displays.Where(d => d.Bounds.Intersect(v).Area > 0).OrderByDescending(d => d.Bounds.Intersect(v).Area).FirstOrDefault();

        string location;
        if (host == null) location = Loc.T("Location_Offscreen");
        else location = Loc.F("Location_Display", _app.Displays.NumberOf(host));

        double scale = host?.Scale ?? 1.0;
        if (w.IsHung) issues.Add(Loc.T("Issue_NotResponding"));
        // 一覧では発見のため、サイズ固定の窓も極小として示す（自動拡大はサイズ変更可能な通常窓だけ）
        if (PlacementCalculator.IsTiny(v, scale, o)) issues.Add(Loc.T("Issue_Tiny"));
        if (w.State != WindowShowState.Maximized)
        {
            bool InAny(int x, int y) => displays.Any(d => d.Bounds.Contains(x, y));
            bool fullyOnScreen = InAny(v.X, v.Y) && InAny(v.Right - 1, v.Y) && InAny(v.X, v.Bottom - 1) && InAny(v.Right - 1, v.Bottom - 1);
            if (host != null && !fullyOnScreen) issues.Add(Loc.T("Issue_PartiallyOffscreen"));
            if (host == null || !WindowJudge.IsOperable(v, active, scale)) issues.Add(Loc.T("Issue_NotOperable"));
        }
        else if (host == null || !active.Any(a => a.Id == host.Id))
        {
            issues.Add(Loc.T("Issue_NotOperable"));
        }
        return new WindowRow { Info = w, Location = location, IssueList = issues };
    }

    private void RefreshDestinations()
    {
        var current = (DestinationCombo.SelectedItem as DisplayItem)?.Display.Id;
        var dest = _app.Displays.GetDestination(AppController.Cursor());
        var items = _app.Displays.Connected.Select(d => new DisplayItem(d, RescuePanelWindow.LabelFor(_app, d))).ToList();
        DestinationCombo.ItemsSource = items;
        DestinationCombo.SelectedItem = items.FirstOrDefault(i => i.Display.Id == current)
            ?? items.FirstOrDefault(i => i.Display.Id == dest?.Id) ?? items.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        string q = SearchBox.Text.Trim();
        IEnumerable<WindowRow> rows = _rows;
        if (ProblemsOnly.IsChecked == true) rows = rows.Where(r => r.HasIssue);
        if (q.Length > 0)
            rows = rows.Where(r => r.AppName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || r.ProcessName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || r.Info.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        var list = rows.ToList();
        Grid.ItemsSource = list;
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = Loc.Plural("List_Count", list.Count, _rows.Count(r => r.HasIssue));
        RescueButton.IsEnabled = Grid.SelectedItems.Count > 0;
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (WindowRow r in e.RemovedItems) _selectionOrder.Remove(r);
        foreach (WindowRow r in e.AddedItems) { _selectionOrder.Remove(r); _selectionOrder.Add(r); }
        RescueButton.IsEnabled = Grid.SelectedItems.Count > 0;
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            RescueSelected();
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => RescueSelected();

    private void Rescue_Click(object sender, RoutedEventArgs e) => RescueSelected();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    /// <summary>選択した窓を救出する。最後に選んだ窓を操作対象（前面）にする。</summary>
    private void RescueSelected()
    {
        if (DestinationCombo.SelectedItem is not DisplayItem dest)
        {
            Banner.Show(StatusKind.Warning, Loc.T("List_NoDestination"));
            return;
        }
        var selected = Grid.SelectedItems.Cast<WindowRow>().ToList();
        if (selected.Count == 0)
        {
            Banner.Show(StatusKind.Info, Loc.T("List_SelectFirst"));
            return;
        }
        var ordered = _selectionOrder.Where(selected.Contains).Concat(selected.Except(_selectionOrder)).Select(r => r.Info).ToList();
        var here = WindowPositioner.DisplayOf(this, _app.Displays.Connected) ?? dest.Display;
        _app.RescueWindows(ordered, dest.Display, here, show: true, activateLast: true);
    }
}
