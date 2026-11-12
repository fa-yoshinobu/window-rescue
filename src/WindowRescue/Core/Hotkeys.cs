using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using WindowRescue.Native;

namespace WindowRescue.Core;

/// <summary>"Ctrl+Alt+Shift+1" 形式のキー割り当て。</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public bool IsValid => Key != Key.None && Modifiers != ModifierKeys.None;

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = ModifierKeys.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModifierKeys.Control; break;
                case "alt": mods |= ModifierKeys.Alt; break;
                case "shift": mods |= ModifierKeys.Shift; break;
                case "win": case "windows": mods |= ModifierKeys.Windows; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (raw.Equals("Backspace", StringComparison.OrdinalIgnoreCase)) key = Key.Back;
                    else if (!Enum.TryParse(raw, true, out key)) return false;
                    break;
            }
        }
        gesture = new HotkeyGesture(mods, key);
        return gesture.IsValid;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public string ToDisplayString() => ToString().Replace("+", " + ");

    public static string KeyName(Key k) => k switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (k - Key.D0))).ToString(),
        Key.Back => "Backspace",
        Key.Return => "Enter",
        _ => k.ToString(),
    };

    internal uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? NativeMethods.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Alt) ? NativeMethods.MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? NativeMethods.MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? NativeMethods.MOD_WIN : 0) |
        NativeMethods.MOD_NOREPEAT;
}

/// <summary>ショートカットの操作 ID と初期割り当て。</summary>
public static class HotkeyActions
{
    public const string RescueForeground = "RescueForeground";
    public const string RescueAll = "RescueAll";
    public const string OpenList = "OpenList";
    public const string RescueHere = "RescueHere";

    public static string ForDisplay(string action, int number) => $"{action}:{number}";

    public static bool TryParseDisplayAction(string id, out string action, out int number)
    {
        action = id;
        number = 0;
        int i = id.IndexOf(':');
        if (i < 0) return false;
        action = id[..i];
        return int.TryParse(id[(i + 1)..], out number);
    }

    /// <summary>接続中の画面番号と、常に表示する 1〜3 の操作 ID。</summary>
    public static List<string> All(IEnumerable<int> connectedNumbers)
    {
        var numbers = connectedNumbers.Concat([1, 2, 3]).Where(n => n > 0).Distinct().Order().ToList();
        var list = new List<string>();
        foreach (int number in numbers) list.Add(ForDisplay(RescueForeground, number));
        foreach (int number in numbers) list.Add(ForDisplay(RescueAll, number));
        list.Add(OpenList);
        list.Add(RescueHere);
        return list;
    }

    public static string Default(string id, bool hasAltGr = false)
    {
        string gesture;
        if (TryParseDisplayAction(id, out var action, out int n))
        {
            if (n > 9) return "";
            gesture = action == RescueForeground ? $"Ctrl+Alt+{n}" : $"Ctrl+Alt+Shift+{n}";
        }
        else gesture = id switch
        {
            OpenList => "Ctrl+Alt+Space",
            RescueHere => "Ctrl+Alt+Home",
            _ => "",
        };
        return KeyboardLayout.ConflictsWithAltGr(gesture, hasAltGr) ? "" : gesture;
    }

    internal static string Resolve(string action, IReadOnlyDictionary<string, string> configured, bool hasAltGr) =>
        configured.TryGetValue(action, out var gesture) ? gesture : Default(action, hasAltGr);
}

public enum HotkeyStatus { Unassigned, Registered, Failed, Invalid, Duplicate }

/// <summary>RegisterHotKey によるグローバルショートカット。UI スレッドで使う。</summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, string> _idToAction = [];
    private int _nextId = 1;

    public event Action<string>? Pressed;
    public Dictionary<string, (HotkeyStatus Status, string Detail)> Status { get; } = [];

    public HotkeyService()
    {
        var p = new HwndSourceParameters("WindowRescueHotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */, Width = 0, Height = 0 };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    public void Apply(IEnumerable<string> actions, Func<string, string> gestureFor)
    {
        UnregisterAll();
        Status.Clear();
        var used = new Dictionary<HotkeyGesture, string>();
        foreach (var action in actions)
        {
            string text = gestureFor(action);
            if (string.IsNullOrWhiteSpace(text)) { Status[action] = (HotkeyStatus.Unassigned, ""); continue; }
            if (!HotkeyGesture.TryParse(text, out var g)) { Status[action] = (HotkeyStatus.Invalid, text); continue; }
            if (used.TryGetValue(g, out var other)) { Status[action] = (HotkeyStatus.Duplicate, other); continue; }

            int id = _nextId++;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(g.Key);
            if (NativeMethods.RegisterHotKey(_source.Handle, id, g.NativeModifiers, vk))
            {
                _idToAction[id] = action;
                used[g] = action;
                Status[action] = (HotkeyStatus.Registered, "");
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                Status[action] = (HotkeyStatus.Failed, new System.ComponentModel.Win32Exception(err).Message);
            }
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _idToAction.Keys) NativeMethods.UnregisterHotKey(_source.Handle, id);
        _idToAction.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _idToAction.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
