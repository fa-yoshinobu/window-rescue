using System.IO;
using System.IO.Pipes;

using System.Security.Principal;
using System.Text;

namespace WindowRescue.Core;

/// <summary>起動引数・プロセス間で受け渡す固定の操作。</summary>
public sealed record AppCommand(string Action, int CursorX, int CursorY)
{
    public const string RescueHere = "rescue-here";
    public const string OpenList = "list";
    public const string OpenSettings = "settings";
    public const string OpenPanel = "panel";
    public const string ShowMini = "mini";
    public const string None = "none";

    private static readonly HashSet<string> Known = [RescueHere, OpenList, OpenSettings, OpenPanel, ShowMini, None];

    /// <summary>"--action rescue-here" などを解析する。カーソル位置は実行要求を受けた時点の値を使う。</summary>
    public static AppCommand FromArgs(string[] args, int cursorX, int cursorY)
    {
        string action = None;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--action" && i + 1 < args.Length) action = args[i + 1];
        }
        if (!Known.Contains(action)) action = None;
        return new AppCommand(action, cursorX, cursorY);
    }

    public string Serialize() => $"{Action}|{CursorX}|{CursorY}";

    public static AppCommand? Deserialize(string line)
    {
        var p = line.Split('|');
        if (p.Length != 3 || !Known.Contains(p[0])) return null;
        if (!int.TryParse(p[1], out int x) || !int.TryParse(p[2], out int y)) return null;
        return new AppCommand(p[0], x, y);
    }
}

/// <summary>同じ利用者の常駐プロセスを 1 つに保ち、名前付きパイプで操作を渡す。</summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string UserSid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    private static readonly string MutexName = $@"Local\WindowRescue_{UserSid}";
    private static readonly string PipeName = $"WindowRescue_{UserSid}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();
    public bool IsPrimary { get; }

    public SingleInstance(TimeSpan wait)
    {
        _mutex = new Mutex(false, MutexName);
        try
        {
            IsPrimary = _mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
    }

    /// <summary>既存プロセスへ操作を送る。起動直後でまだ受け付けていない場合は少し待って再試行する。</summary>
    public static bool Send(AppCommand command)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                client.Connect(500);
                var bytes = Encoding.UTF8.GetBytes(command.Serialize());
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                return true;
            }
            catch (Exception) when (attempt < 9)
            {
                Thread.Sleep(300);
            }
            catch (Exception)
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>受信ループを開始する。onCommand はスレッドプールから呼ばれる。</summary>
    public void StartServer(Action<AppCommand> onCommand)
    {
        var token = _cts.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // CurrentUserOnly：同じ利用者のプロセスだけが接続できる
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token);
                    var buffer = new byte[256];
                    int n = await server.ReadAsync(buffer, token);
                    var cmd = AppCommand.Deserialize(Encoding.UTF8.GetString(buffer, 0, n).Trim());
                    if (cmd != null) onCommand(cmd);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(200, CancellationToken.None);
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary)
        {
            try { _mutex.ReleaseMutex(); } catch (Exception) { }
        }
        _mutex.Dispose();
    }
}
