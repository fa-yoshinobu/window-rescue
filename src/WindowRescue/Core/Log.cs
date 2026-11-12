using System.Text;
using WindowRescue.Settings;

namespace WindowRescue.Core;

public static class Log
{
    public static string FilePath => Path.Combine(SettingsStore.Directory, "WindowRescue.log");
    private static readonly RotatingLog Writer = new(FilePath);

    public static void Error(string operation, Exception exception) => Writer.Error(operation, exception);

    internal static void RescueFailed(WindowInfo window, WindowResult result)
    {
        if (result.Outcome == RescueOutcome.Failed)
            Writer.Write($"Rescue failed: process={window.ProcessName}; class={window.ClassName}; reason={result.Reason}");
    }
}

internal sealed class RotatingLog(string filePath, int maxBytes = 1024 * 1024)
{
    private readonly object _gate = new();

    public void Error(string operation, Exception exception)
    {
        try
        {
            // 例外メッセージにも窓のタイトルが含まれ得るため、型・コード・スタックだけを記録する。
            var text = new StringBuilder(operation);
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                text.Append($"\n{e.GetType().FullName} (0x{e.HResult:X8})\n{e.StackTrace}");
                if (text.Length >= maxBytes) break;
            }
            Write(text.ToString());
        }
        catch (Exception) { } // 障害時のログ整形失敗も外へ漏らさない。
    }

    public void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var entry = $"{DateTimeOffset.Now:O} {message}";
                var bytes = new byte[Math.Min(maxBytes, Encoding.UTF8.GetByteCount(entry) + 1)];
                Encoding.UTF8.GetEncoder().Convert(entry.AsSpan(), bytes.AsSpan(0, bytes.Length - 1), true,
                    out _, out int written, out _);
                bytes[written++] = (byte)'\n';
                if (File.Exists(filePath) && new FileInfo(filePath).Length + written > maxBytes)
                    File.Move(filePath, filePath + ".1", true);
                using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes, 0, written);
            }
        }
        catch (Exception)
        {
            // 容量不足・アクセス拒否など、ログ保存の失敗をアプリの障害にしない。
        }
    }
}
