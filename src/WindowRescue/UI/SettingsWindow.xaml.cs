using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WindowRescue.Core;
using WindowRescue.Localization;
using WindowRescue.Settings;

namespace WindowRescue.UI;

public partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private bool _updating;

    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    public SettingsWindow(AppController app)
    {
        InitializeComponent();
        _app = app;
        _app.StateChanged += Reload;
        _app.HotkeysChanged += BuildShortcuts;
        Loc.Instance.LanguageChanged += Reload;
        Closed += (_, _) =>
        {
            _app.StateChanged -= Reload;
            _app.HotkeysChanged -= BuildShortcuts;
            Loc.Instance.LanguageChanged -= Reload;
        };
    }

    private DisplayInfo Here => WindowPositioner.DisplayOf(this, _app.Displays.Connected) ?? _app.UiDisplayAt(AppController.Cursor());
    private (int, int) HerePoint => (Here.Bounds.CenterX, Here.Bounds.CenterY);

    public void Reload()
    {
        _updating = true;
        try
        {
            var s = _app.Settings;

            ModeOff.IsChecked = s.Mode == MonitorMode.Off;
            ModeAuto.IsChecked = s.Mode == MonitorMode.AutoRescue;
            ModeFixed.IsChecked = s.Mode == MonitorMode.FixedDestination;
            AutoResizeCheck.IsChecked = s.AutoResize;
            AutoResizeText.Text = Loc.F("Settings_AutoResize", Loc.T(s.AutoResize ? "Common_On" : "Common_Off"));
            TinyW.Text = s.TinyWidthDip.ToString();
            TinyH.Text = s.TinyHeightDip.ToString();
            StdW.Text = s.StandardWidthDip.ToString();
            StdH.Text = s.StandardHeightDip.ToString();

            ExclusionScope.ItemsSource = new[]
            {
                new Choice(nameof(Settings.ExclusionScope.All), Loc.T("Exclusion_All")),
                new Choice(nameof(Settings.ExclusionScope.SizeOnly), Loc.T("Exclusion_SizeOnly")),
            };
            ExclusionScope.DisplayMemberPath = nameof(Choice.Label);
            ExclusionScope.SelectedIndex = 0;
            BuildExclusions();
            BuildShortcuts();
            UpdateContextMenuState();

            var langs = new[]
            {
                new Choice("system", Loc.T("Language_System")),
                new Choice("ja", "日本語"),
                new Choice("en", "English"),
            };
            LanguageCombo.ItemsSource = langs;
            LanguageCombo.DisplayMemberPath = nameof(Choice.Label);
            LanguageCombo.SelectedItem = langs.FirstOrDefault(l => l.Value == s.Language) ?? langs[0];

            AutoStartCheck.IsChecked = _app.AutoStartEnabled;
            PrivilegeText.Text = Loc.T(AppController.IsElevated ? "Settings_RunningElevated" : "Settings_RunningStandard");
            ElevateButton.Visibility = AppController.IsElevated ? Visibility.Collapsed : Visibility.Visible;
            SettingsPathText.Text = Loc.F("Settings_StoredAt", SettingsStore.FilePath);
            VersionText.Text = Loc.T("About_Version") + AboutWindow.AppVersion;
        }
        finally
        {
            _updating = false;
        }
    }

    // ───────────── 監視 ─────────────

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _app.Settings.Mode = ModeAuto.IsChecked == true ? MonitorMode.AutoRescue
            : ModeFixed.IsChecked == true ? MonitorMode.FixedDestination : MonitorMode.Off;
        _app.ApplyMode();
        _app.RaiseStateChanged();
    }

    private void AutoResize_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _app.Settings.AutoResize = AutoResizeCheck.IsChecked == true;
        _app.RaiseStateChanged();
    }

    private void Numbers_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        var s = _app.Settings;
        bool ok = TryRange(TinyW.Text, 20, 2000, out int tw) & TryRange(TinyH.Text, 20, 2000, out int th)
                & TryRange(StdW.Text, 200, 8000, out int sw) & TryRange(StdH.Text, 150, 8000, out int sh);
        if (!ok)
        {
            NumbersBanner.Show(StatusKind.Error, Loc.T("Error_InvalidNumber"), Loc.T("Error_InvalidNumberHint"));
            return;
        }
        NumbersBanner.Hide();
        if (s.TinyWidthDip == tw && s.TinyHeightDip == th && s.StandardWidthDip == sw && s.StandardHeightDip == sh) return;
        s.TinyWidthDip = tw; s.TinyHeightDip = th; s.StandardWidthDip = sw; s.StandardHeightDip = sh;
        _app.Save();
    }

    private static bool TryRange(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), out value) && value >= min && value <= max;

    private void BuildExclusions()
    {
        ExclusionRows.Children.Clear();
        if (_app.Settings.Exclusions.Count == 0)
        {
            ExclusionRows.Children.Add(new TextBlock { Text = Loc.T("Settings_NoExclusions"), Style = (Style)FindResource("Help") });
            return;
        }
        foreach (var ex in _app.Settings.Exclusions.ToList())
        {
            var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var remove = new Button { Content = new TextBlock { Text = Loc.T("Common_Remove") }, MinWidth = 72 };
            AutomationProperties.SetName(remove, $"{Loc.T("Common_Remove")} — {ex.ProcessName}");
            remove.Click += (_, _) => { _app.Settings.Exclusions.Remove(ex); _app.RaiseStateChanged(); };
            DockPanel.SetDock(remove, Dock.Right);
            dock.Children.Add(remove);
            dock.Children.Add(new TextBlock
            {
                Text = $"{ex.ProcessName} — {Loc.T(ex.Scope == Settings.ExclusionScope.All ? "Exclusion_All" : "Exclusion_SizeOnly")}",
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            });
            ExclusionRows.Children.Add(dock);
        }
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        string name = ExclusionProcess.Text.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return;
        var scope = (ExclusionScope.SelectedItem as Choice)?.Value == nameof(Settings.ExclusionScope.SizeOnly)
            ? Settings.ExclusionScope.SizeOnly : Settings.ExclusionScope.All;
        var existing = _app.Settings.FindExclusion(name);
        if (existing != null) existing.Scope = scope;
        else _app.Settings.Exclusions.Add(new AppExclusion { ProcessName = name, Scope = scope });
        ExclusionProcess.Text = "";
        _app.RaiseStateChanged();
    }

    // ───────────── ショートカット ─────────────

    private void BuildShortcuts()
    {
        ShortcutGrid.Children.Clear();
        ShortcutGrid.RowDefinitions.Clear();
        int row = 0;
        foreach (var action in _app.HotkeyActionIds)
        {
            ShortcutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string name = ActionName(action);

            var label = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 12, 8) };
            Grid.SetRow(label, row);
            ShortcutGrid.Children.Add(label);

            var stack = new StackPanel { Margin = new Thickness(0, 4, 12, 4) };
            string gesture = _app.GestureFor(action);
            var box = new TextBox
            {
                Text = HotkeyGesture.TryParse(gesture, out var g) ? g.ToDisplayString() : Loc.T("Shortcut_None"),
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                Tag = action,
            };
            AutomationProperties.SetName(box, name);
            AutomationProperties.SetHelpText(box, Loc.T("Shortcut_CaptureHelp"));
            box.PreviewKeyDown += Shortcut_PreviewKeyDown;
            stack.Children.Add(box);

            var (status, detail) = _app.Hotkeys.Status.TryGetValue(action, out var st) ? st : (HotkeyStatus.Unassigned, "");
            var statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Help") };
            switch (status)
            {
                case HotkeyStatus.Registered:
                    statusText.Text = "✓ " + Loc.T("Shortcut_Registered");
                    statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Success");
                    break;
                case HotkeyStatus.Failed:
                    statusText.Text = "⚠ " + Loc.T("Shortcut_Failed") + (detail.Length > 0 ? $" ({detail})" : "");
                    statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
                    break;
                case HotkeyStatus.Duplicate:
                    statusText.Text = "⚠ " + Loc.F("Shortcut_Duplicate", ActionName(detail));
                    statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
                    break;
                case HotkeyStatus.Invalid:
                    statusText.Text = "⚠ " + Loc.T("Shortcut_Invalid");
                    statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Error");
                    break;
                default:
                    statusText.Text = Loc.T("Shortcut_Unassigned");
                    break;
            }
            stack.Children.Add(statusText);
            Grid.SetRow(stack, row);
            Grid.SetColumn(stack, 1);
            ShortcutGrid.Children.Add(stack);

            var clear = new Button { Content = new TextBlock { Text = Loc.T("Shortcut_Clear") }, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 4), MinWidth = 72 };
            AutomationProperties.SetName(clear, $"{Loc.T("Shortcut_Clear")} — {name}");
            clear.Click += (_, _) => SetGesture(action, "");
            Grid.SetRow(clear, row);
            Grid.SetColumn(clear, 2);
            ShortcutGrid.Children.Add(clear);
            row++;
        }
    }

    private static string ActionName(string action)
    {
        if (HotkeyActions.TryParseDisplayAction(action, out var a, out int n))
            return Loc.F(a == HotkeyActions.RescueForeground ? "Action_RescueForeground" : "Action_RescueAll", n);
        return Loc.T("Action_" + action);
    }

    /// <summary>キー入力で割り当てを取り込む。修飾キーなしの Tab はフォーカス移動に使う。</summary>
    private void Shortcut_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.None && (key == Key.Tab || key == Key.Escape)) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        if (mods == ModifierKeys.None && key is Key.Delete or Key.Back)
        {
            SetGesture((string)box.Tag, "");
            return;
        }
        var g = new HotkeyGesture(mods, key);
        if (!g.IsValid)
        {
            box.Text = Loc.T("Shortcut_NeedModifier");
            return;
        }
        SetGesture((string)box.Tag, g.ToString());
    }

    private void SetGesture(string action, string gesture)
    {
        _app.Settings.Hotkeys[action] = gesture;
        _app.Save();
        _app.ApplyHotkeys();
        // 再構築後も同じ行にフォーカスを戻す
        var box = ShortcutGrid.Children.OfType<StackPanel>().SelectMany(s => s.Children.OfType<TextBox>()).FirstOrDefault(t => (string)t.Tag == action);
        box?.Focus();
    }

    private void ResetShortcuts_Click(object sender, RoutedEventArgs e)
    {
        _app.Settings.Hotkeys.Clear();
        _app.Save();
        _app.ApplyHotkeys();
    }

    // ───────────── 右クリックメニュー ─────────────

    private void UpdateContextMenuState()
    {
        var state = _app.ContextMenuState;
        string? err = _app.LastContextMenuError;
        if (err != null)
        {
            ContextMenuBanner.Show(StatusKind.Error, Loc.T("ContextMenu_Failed"),
                Loc.F("ContextMenu_CurrentState", Loc.T("ContextMenu_" + state)), err);
        }
        else
        {
            var kind = state switch
            {
                ContextMenuState.Registered => StatusKind.Success,
                ContextMenuState.NeedsRepair => StatusKind.Warning,
                _ => StatusKind.Info,
            };
            ContextMenuBanner.Show(kind, Loc.T("ContextMenu_" + state), Loc.T("ContextMenu_" + state + "_Help"));
        }
        RegisterButtonText.Text = Loc.T(state == ContextMenuState.NeedsRepair ? "Settings_RegisterRepair" : "Settings_Register");
        RegisterButton.IsEnabled = state != ContextMenuState.Registered;
        UnregisterButton.IsEnabled = state != ContextMenuState.NotRegistered;
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        _app.RegisterContextMenu();
        UpdateContextMenuState();
        UnregisterButton.Focus();
    }

    private void Unregister_Click(object sender, RoutedEventArgs e)
    {
        _app.UnregisterContextMenu();
        UpdateContextMenuState();
        RegisterButton.Focus();
    }

    // ───────────── 言語・起動 ─────────────

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || LanguageCombo.SelectedItem is not Choice c || c.Value == _app.Settings.Language) return;
        _app.SetLanguage(c.Value);
        LanguageCombo.Focus();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _app.SetAutoStart(AutoStartCheck.IsChecked == true);
    }

    private void Elevate_Click(object sender, RoutedEventArgs e) => _app.RestartElevated();

    // ───────────── 上部の操作 ─────────────

    private void RescueAll_Click(object sender, RoutedEventArgs e)
    {
        var dest = _app.Displays.GetDestination(AppController.Cursor());
        if (dest != null) _app.RescueAllTo(dest, Here);
    }

    private void OpenList_Click(object sender, RoutedEventArgs e) => _app.OpenList(HerePoint);

    private void About_Click(object sender, RoutedEventArgs e) => _app.OpenAbout(HerePoint);

    private void OpenPanel_Click(object sender, RoutedEventArgs e) => _app.OpenPanel(HerePoint);
}
