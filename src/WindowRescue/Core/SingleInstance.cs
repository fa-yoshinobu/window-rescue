using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
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
    private static readonly SecurityIdentifier UserSid = WindowsIdentity.GetCurrent().User
        ?? throw new InvalidOperationException("The current user SID is unavailable.");
    private static readonly string MutexName = $@"Local\WindowRescue_{UserSid}";
    internal static readonly string PipeName = $"WindowRescue_{UserSid}";

    private readonly Mutex? _mutex;
    private readonly CancellationTokenSource _cts = new();
    public bool IsPrimary { get; }

    public SingleInstance(TimeSpan wait)
        : this(wait, () => MutexAcl.Create(false, MutexName, out _, CreateMutexSecurity())) { }

    internal SingleInstance(TimeSpan wait, Func<Mutex> openMutex)
    {
        try
        {
            _mutex = openMutex();
            IsPrimary = _mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }
        catch (UnauthorizedAccessException ex)
        {
            // 既存の旧版などで開けない場合も、二重に常駐せずパイプへの送信を試す。
            Log.Error("Single-instance mutex access denied", ex);
        }
    }

    internal static MutexSecurity CreateMutexSecurity()
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(UserSid, MutexRights.FullControl, AccessControlType.Allow));
        security.SetOwner(UserSid);
        return security;
    }

    internal static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(UserSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        security.SetOwner(UserSid);
        return security;
    }

    public enum SendFailure { None, AccessDenied, Timeout, ConnectionLost }

    public static bool Send(AppCommand command) => Send(command, out _);

    /// <summary>接続と書込みを合わせて最大 2.5 秒待つ。UI 起動前にも呼べる。</summary>
    public static bool Send(AppCommand command, out SendFailure failure)
    {
        failure = SendAsync(command, PipeName).GetAwaiter().GetResult();
        return failure == SendFailure.None;
    }

    internal static async Task<SendFailure> SendAsync(AppCommand command, string pipeName)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(2500));
        try
        {
            // 識別だけを許可し、接続先へクライアントの権限を貸さない。
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            await client.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await client.WriteAsync(Encoding.UTF8.GetBytes(command.Serialize()), deadline.Token).ConfigureAwait(false);
            await client.FlushAsync(deadline.Token).ConfigureAwait(false);
            return SendFailure.None;
        }
        catch (UnauthorizedAccessException ex) { Log.Error("Command send access denied", ex); return SendFailure.AccessDenied; }
        catch (OperationCanceledException ex) { Log.Error("Command send timed out", ex); return SendFailure.Timeout; }
        catch (IOException ex) { Log.Error("Command send failed", ex); return SendFailure.ConnectionLost; }
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
                    using var server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Message, PipeOptions.Asynchronous, 256, 0, CreatePipeSecurity());
                    await server.WaitForConnectionAsync(token);
                    using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    requestDeadline.CancelAfter(TimeSpan.FromMilliseconds(2500));
                    var buffer = new byte[256];
                    int n = await server.ReadAsync(buffer, requestDeadline.Token);
                    if (n == 0 || !server.IsMessageComplete) continue;
                    bool sameUser = false;
                    server.RunAsClient(() =>
                    {
                        using var clientIdentity = WindowsIdentity.GetCurrent(ifImpersonating: true);
                        sameUser = clientIdentity?.User is { } clientSid && UserSid.Equals(clientSid);
                    });
                    if (!sameUser) continue;
                    // 同じ SID でも受け付けるのは Known の固定操作だけ。コマンド・パスを実行せず、
                    // トークンや任意のファイル操作も公開しないため、送信元へ管理者権限を渡さない。
                    var cmd = AppCommand.Deserialize(Encoding.UTF8.GetString(buffer, 0, n).Trim());
                    if (cmd != null) onCommand(cmd);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) { /* 応答しない接続だけを閉じ、次の要求を受け付ける。 */ }
                catch (Exception ex)
                {
                    Log.Error("Command receive failed", ex);
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
            try { _mutex?.ReleaseMutex(); } catch (Exception) { }
        }
        _mutex?.Dispose();
    }
}
