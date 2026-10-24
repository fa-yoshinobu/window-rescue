using Microsoft.Win32;
using WindowRescue.Native;

namespace WindowRescue.Core;

public enum ContextMenuState { NotRegistered, Registered, NeedsRepair }

/// <summary>デスクトップ右クリックメニュー（従来形式）の登録・解除（第5.3節）。現在のユーザーだけに登録する。</summary>
public static class ContextMenuRegistrar
{
    public const string ParentPath = @"Software\Classes\DesktopBackground\Shell";
    public const string KeyName = "WindowRescue";
    public const string KeyPath = ParentPath + @"\" + KeyName;
    private const string OwnerValue = "WindowRescueOwner";

    /// <summary>メニュー項目（キー名は言語に依存しない）。Label はリソースキー。</summary>
    public static readonly (string Key, string Action, string Label)[] Items =
    [
        ("01RescueHere", AppCommand.RescueHere, "Menu_RescueHere"),
        ("02ShowMini", AppCommand.ShowMini, "Menu_ShowMini"),
        ("03List", AppCommand.OpenList, "Menu_OpenList"),
        ("04Settings", AppCommand.OpenSettings, "Menu_OpenSettings"),
    ];

    public static string CommandFor(string exePath, string action) => $"\"{exePath}\" --action {action}";

    public static ContextMenuState GetState(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key == null) return ContextMenuState.NotRegistered;
        if (key.GetValue(OwnerValue) as string != "1") return ContextMenuState.NeedsRepair;
        foreach (var (k, action, _) in Items)
        {
            using var cmd = key.OpenSubKey($@"shell\{k}\command");
            if (cmd?.GetValue(null) is not string value ||
                !string.Equals(value, CommandFor(exePath, action), StringComparison.OrdinalIgnoreCase))
                return ContextMenuState.NeedsRepair;
        }
        return ContextMenuState.Registered;
    }

    /// <summary>登録（既存の本アプリ登録は作り直す）。失敗時は変更前の状態へ戻して例外を投げる。</summary>
    public static void Register(string exePath, string menuTitle, Func<string, string> label)
    {
        var backup = Snapshot(KeyPath);
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true)
                ?? throw new UnauthorizedAccessException(KeyPath);
            key.SetValue("MUIVerb", menuTitle);
            key.SetValue("Icon", $"\"{exePath}\",0");
            key.SetValue("SubCommands", "");
            key.SetValue(OwnerValue, "1");
            foreach (var (k, action, labelKey) in Items)
            {
                using var item = key.CreateSubKey($@"shell\{k}", true);
                item.SetValue("MUIVerb", label(labelKey));
                using var cmd = item.CreateSubKey("command", true);
                cmd.SetValue(null, CommandFor(exePath, action));
            }
            if (GetState(exePath) != ContextMenuState.Registered)
                throw new InvalidOperationException("verification failed");
        }
        catch
        {
            TryRestore(backup);
            throw;
        }
        finally
        {
            NotifyShell();
        }
    }

    /// <summary>本アプリのキーだけを削除する。未登録でも成功扱い。</summary>
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
        NotifyShell();
    }

    /// <summary>登録済みの場合だけ、表示文字列を更新する。登録先・コマンドは変えない。</summary>
    public static void UpdateLabels(string menuTitle, Func<string, string> label)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true);
        if (key == null) return;
        key.SetValue("MUIVerb", menuTitle);
        foreach (var (k, _, labelKey) in Items)
        {
            using var item = key.OpenSubKey($@"shell\{k}", true);
            item?.SetValue("MUIVerb", label(labelKey));
        }
    }

    private static void NotifyShell() =>
        NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private sealed record KeySnapshot(string Path, List<(string Name, object Value, RegistryValueKind Kind)> Values, List<KeySnapshot> Children);

    private static KeySnapshot? Snapshot(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key == null) return null;
        var values = key.GetValueNames()
            .Select(n => (n, key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!, key.GetValueKind(n)))
            .ToList();
        var children = key.GetSubKeyNames().Select(c => Snapshot($@"{path}\{c}")).OfType<KeySnapshot>().ToList();
        return new KeySnapshot(path, values, children);
    }

    private static void TryRestore(KeySnapshot? backup)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath, false);
            if (backup != null) Write(backup);
        }
        catch (Exception)
        {
            // 戻せない場合は、呼び出し元が状態を再確認して表示する
        }

        static void Write(KeySnapshot s)
        {
            using var key = Registry.CurrentUser.CreateSubKey(s.Path, true);
            foreach (var (name, value, kind) in s.Values) key.SetValue(name, value, kind);
            foreach (var c in s.Children) Write(c);
        }
    }
}

/// <summary>Windows 起動時の自動起動（HKCU\...\Run）。</summary>
public static class AutoStart
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WindowRescue";

    public static bool IsEnabled(string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key?.GetValue(ValueName) is string v && v.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(string exePath, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        if (enabled) key.SetValue(ValueName, $"\"{exePath}\" --background");
        else key.DeleteValue(ValueName, false);
    }
}
