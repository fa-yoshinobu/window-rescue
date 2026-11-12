using System.Collections.Concurrent;
using System.Windows.Input;
using WindowRescue.Native;

namespace WindowRescue.Core;

public static class KeyboardLayout
{
    private static readonly ConcurrentDictionary<IntPtr, bool> AltGrLayouts = new();

    public static bool CurrentHasAltGr()
    {
        uint thread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        return HasAltGr(NativeMethods.GetKeyboardLayout(thread));
    }

    internal static bool HasAltGr(IntPtr layout) =>
        AltGrLayouts.GetOrAdd(layout, h => DetectAltGr(c => NativeMethods.VkKeyScanEx(c, h)));

    internal static bool DetectAltGr(Func<char, short> keyScan)
    {
        // VkKeyScanEx は AltGr を Ctrl+Alt（シフト状態の下位ビット 6）として返す。
        for (int code = 32; code <= char.MaxValue; code++)
        {
            if (char.IsSurrogate((char)code)) continue;
            short mapping = keyScan((char)code);
            if (mapping != -1 && ((mapping >> 8) & 6) == 6) return true;
        }
        return false;
    }

    public static bool ConflictsWithAltGr(string gesture, bool hasAltGr) =>
        hasAltGr && HotkeyGesture.TryParse(gesture, out var g)
        && g.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Alt)
        && !g.Modifiers.HasFlag(ModifierKeys.Windows)
        && (g.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space
            or >= Key.NumPad0 and <= Key.Divide || g.Key.ToString().StartsWith("Oem", StringComparison.Ordinal));
}
