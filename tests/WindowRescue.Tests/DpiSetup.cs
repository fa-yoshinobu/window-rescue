using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WindowRescue.Tests;

/// <summary>本体アプリ（manifest で PerMonitorV2）と同じ座標系で確認するため、テストプロセスも PerMonitorV2 にする。</summary>
internal static class DpiSetup
{
    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

    public static bool Applied { get; private set; }

    [ModuleInitializer]
    internal static void Init()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        Applied = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) == 2;
    }
}
