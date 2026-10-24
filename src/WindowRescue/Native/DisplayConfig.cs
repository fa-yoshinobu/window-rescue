using System.Runtime.InteropServices;

namespace WindowRescue.Native;

/// <summary>QueryDisplayConfig でモニターの表示名と固有パスを取得する。</summary>
internal static class DisplayConfig
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation;
        public int scaling; public uint refreshNum; public uint refreshDen; public int scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_INFO_HEADER { public int type; public int size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SOURCE_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TARGET_DEVICE_NAME
    {
        public DEVICE_INFO_HEADER header; public uint flags; public int outputTechnology; public ushort edidManufactureId;
        public ushort edidProductCodeId; public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int GET_SOURCE_NAME = 1;
    private const int GET_TARGET_NAME = 2;

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME requestPacket);

    public readonly record struct TargetName(string FriendlyName, string DevicePath);

    /// <summary>GDI デバイス名（\\.\DISPLAY1 など）→ 表示名と固有パス。取得できない環境では空を返す。</summary>
    public static Dictionary<string, TargetName> GetTargetNames()
    {
        var result = new Dictionary<string, TargetName>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint np, out uint nm) != 0) return result;
            var paths = new PATH_INFO[np];
            var modes = new MODE_INFO[nm];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;

            for (int i = 0; i < np; i++)
            {
                var src = new SOURCE_DEVICE_NAME
                {
                    header = new DEVICE_INFO_HEADER
                    {
                        type = GET_SOURCE_NAME, size = Marshal.SizeOf<SOURCE_DEVICE_NAME>(),
                        adapterId = paths[i].sourceInfo.adapterId, id = paths[i].sourceInfo.id,
                    },
                    viewGdiDeviceName = "",
                };
                if (DisplayConfigGetDeviceInfo(ref src) != 0) continue;

                var tgt = new TARGET_DEVICE_NAME
                {
                    header = new DEVICE_INFO_HEADER
                    {
                        type = GET_TARGET_NAME, size = Marshal.SizeOf<TARGET_DEVICE_NAME>(),
                        adapterId = paths[i].targetInfo.adapterId, id = paths[i].targetInfo.id,
                    },
                    monitorFriendlyDeviceName = "", monitorDevicePath = "",
                };
                if (DisplayConfigGetDeviceInfo(ref tgt) != 0) continue;

                // ミラー表示では同じソースに複数ターゲットがあるため、最初のものを使う
                result.TryAdd(src.viewGdiDeviceName, new TargetName(tgt.monitorFriendlyDeviceName, tgt.monitorDevicePath));
            }
        }
        catch (Exception)
        {
            // 取得できなくても EnumDisplayDevices の情報で動作を続ける
        }
        return result;
    }
}
