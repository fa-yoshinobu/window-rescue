using System.Windows;
using System.Windows.Controls;
using WindowRescue.Core;
using WindowRescue.Localization;

namespace WindowRescue.UI;

/// <summary>コンボボックス用の画面項目。</summary>
public sealed record DisplayItem(DisplayInfo Display, string Label)
{
    // 読み上げ・UI Automation の名前に使われる
    public override string ToString() => Label;
}

public partial class RescuePanelWindow : Window
{
    private readonly AppController _app;
    private bool _updating;

    /// <summary>この画面（パネルを開いた時点のカーソルのある画面）。</summary>
    public DisplayInfo? OpenedOn { get; set; }

    public RescuePanelWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.StateChanged += Refresh;
        Loc.Instance.LanguageChanged += Refresh;
        Closed += (_, _) => { _app.StateChanged -= Refresh; Loc.Instance.LanguageChanged -= Refresh; };
    }

    public static string LabelFor(AppController app, DisplayInfo d)
    {
        string name = string.IsNullOrWhiteSpace(d.FriendlyName) ? d.DeviceName : d.FriendlyName;
        return Loc.F("Display_Label", app.Displays.NumberOf(d), name, d.Bounds.Width, d.Bounds.Height, (int)Math.Round(d.Scale * 100), d.WindowsNumber);
    }

    public void Refresh()
    {
        _updating = true;
        var cursor = AppController.Cursor();
        var dest = _app.Displays.GetDestination(cursor);
        var items = _app.Displays.Connected.Select(d => new DisplayItem(d, LabelFor(_app, d))).ToList();
        DestinationCombo.ItemsSource = items;
        DestinationCombo.SelectedItem = items.FirstOrDefault(i => i.Display.Id == dest?.Id);
        if (OpenedOn != null)
            ThisDisplayText.Text = Loc.F("Panel_ThisDisplayIs", _app.Displays.NumberOf(OpenedOn));
        var r = _app.LastReport;
        if (r != null)
            ResultBanner.Show(r.Failed > 0 ? StatusKind.Error : StatusKind.Success, Loc.T("Panel_LastResult"),
                Loc.F("Result_Summary", r.Moved, r.Skipped, r.Failed));
        _updating = false;
    }

    private void DestinationCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || DestinationCombo.SelectedItem is not DisplayItem item) return;
        _app.Displays.SetDestination(item.Display.Id);
        _app.RaiseStateChanged();
    }

    private DisplayInfo Here => WindowPositioner.DisplayOf(this, _app.Displays.Connected) ?? OpenedOn ?? _app.UiDisplayAt(AppController.Cursor());

    private void RescueAll_Click(object sender, RoutedEventArgs e)
    {
        if (DestinationCombo.SelectedItem is DisplayItem item) _app.RescueAllTo(item.Display, Here);
    }

    private void RescueHere_Click(object sender, RoutedEventArgs e)
    {
        var here = OpenedOn ?? Here;
        _app.RescueAllHere((here.Bounds.CenterX, here.Bounds.CenterY));
    }

    private void OpenList_Click(object sender, RoutedEventArgs e)
    {
        var here = Here;
        _app.OpenList((here.Bounds.CenterX, here.Bounds.CenterY));
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var here = Here;
        _app.OpenSettings((here.Bounds.CenterX, here.Bounds.CenterY));
    }
}
