using System.Windows;
using WindowRescue.Core;
using WindowRescue.Localization;
using Forms = System.Windows.Forms;

namespace WindowRescue.UI;

/// <summary>通知領域のアイコンとメニュー（第5.2節）。メニューを開いた時点のカーソル位置を「この画面」として保持する。</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppController _app;
    private readonly Forms.NotifyIcon _icon;
    private (int x, int y) _menuCursor;
    private DisplayInfo? _menuDisplay;
    private Forms.ToolStripMenuItem? _pinItem;

    public TrayIcon(AppController app)
    {
        _app = app;
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _icon.ContextMenuStrip.Opening += (_, _) => OnOpening();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _app.ShowMini(AppController.Cursor());
        };
        RebuildMenu();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/WindowRescue.ico"));
        using var s = info.Stream;
        return new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize);
    }

    /// <summary>メニューを開いた画面に合わせて、「新しい窓をこの画面に表示」の文言とチェックを更新する。</summary>
    private void OnOpening()
    {
        _menuCursor = AppController.Cursor();
        _menuDisplay = _app.UiDisplayAt(_menuCursor);
        if (_pinItem == null) return;
        _pinItem.Text = Loc.F("Menu_PinNewWindows", _app.Displays.NumberOf(_menuDisplay));
        _pinItem.Checked = _app.IsNewWindowsPinnedTo(_menuDisplay);
    }

    public void RebuildMenu()
    {
        _icon.Text = $"{Loc.T("App_Name")} {AboutWindow.AppVersion}";
        var m = _icon.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add(Loc.T("Menu_RescueHere"), null, (_, _) => _app.RescueAllHere(_menuCursor));
        _pinItem = new Forms.ToolStripMenuItem(Loc.F("Menu_PinNewWindows", ""));
        _pinItem.Click += (_, _) =>
        {
            if (_menuDisplay != null) _app.SetNewWindowsPinned(_menuDisplay, !_pinItem.Checked);
        };
        m.Items.Add(_pinItem);
        m.Items.Add(new Forms.ToolStripSeparator());
        m.Items.Add(Loc.T("Menu_ShowMini"), null, (_, _) => _app.ShowMini(_menuCursor, atCursor: true));
        m.Items.Add(Loc.T("Menu_OpenPanel"), null, (_, _) => _app.OpenPanel(_menuCursor));
        m.Items.Add(Loc.T("Menu_OpenList"), null, (_, _) => _app.OpenList(_menuCursor));
        m.Items.Add(Loc.T("Menu_OpenSettings"), null, (_, _) => _app.OpenSettings(_menuCursor));
        if (!AppController.IsElevated)
            m.Items.Add(Loc.T("Menu_RestartElevated"), null, (_, _) => _app.RestartElevated());
        m.Items.Add(Loc.T("Menu_About"), null, (_, _) => _app.OpenAbout(_menuCursor));
        m.Items.Add(new Forms.ToolStripSeparator());
        m.Items.Add(Loc.T("Menu_Exit"), null, (_, _) => _app.Exit());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
