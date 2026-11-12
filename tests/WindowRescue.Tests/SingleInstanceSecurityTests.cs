using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using WindowRescue.Core;

namespace WindowRescue.Tests;

public class SingleInstanceSecurityTests
{
    [Fact]
    public void ObjectsAllowOnlyTheUserSidAndRejectNetworkClients()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var mutex = SingleInstance.CreateMutexSecurity();
        var pipe = SingleInstance.CreatePipeSecurity();
        Assert.True(mutex.AreAccessRulesProtected);
        Assert.True(pipe.AreAccessRulesProtected);
        var mutexRule = Assert.Single(mutex.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<MutexAccessRule>());
        Assert.Equal(identity.User, mutexRule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, mutexRule.AccessControlType);
        Assert.Equal(MutexRights.FullControl, mutexRule.MutexRights);
        var rules = pipe.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        var allow = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Allow);
        Assert.Equal(identity.User, allow.IdentityReference);
        Assert.Equal(PipeAccessRights.FullControl, allow.PipeAccessRights);
        var deny = Assert.Single(rules, r => r.AccessControlType == AccessControlType.Deny);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), deny.IdentityReference);
        foreach (ObjectSecurity security in new ObjectSecurity[] { mutex, pipe })
        {
            Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        }
    }

    [Fact]
    public void DeniedMutexIsTreatedAsAnExistingInstance()
    {
        using var instance = new SingleInstance(TimeSpan.Zero, () => throw new UnauthorizedAccessException());
        Assert.False(instance.IsPrimary);
    }

    [Fact]
    public async Task MissingServerTimesOutWithinThreeSeconds()
    {
        var timer = Stopwatch.StartNew();
        var result = await SingleInstance.SendAsync(new(AppCommand.OpenList, 0, 0), "WindowRescue_test_" + Guid.NewGuid());
        Assert.Equal(SingleInstance.SendFailure.Timeout, result);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), timer.Elapsed.ToString());
    }

    [Fact]
    public async Task DeniedPipeReportsAccessDenied()
    {
        string name = "WindowRescue_test_" + Guid.NewGuid();
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1,
            PipeTransmissionMode.Message, PipeOptions.Asynchronous, 256, 0, security);
        Assert.Equal(SingleInstance.SendFailure.AccessDenied,
            await SingleInstance.SendAsync(new(AppCommand.OpenList, 0, 0), name));
    }

    [Fact]
    public async Task ServerThatDoesNotReadAlsoTimesOutWithinThreeSeconds()
    {
        string name = "WindowRescue_test_" + Guid.NewGuid();
        using var server = new NamedPipeServerStream(name, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 256, 0);
        var connected = server.WaitForConnectionAsync();
        var timer = Stopwatch.StartNew();
        var send = SingleInstance.SendAsync(new(new string('x', 65536), 0, 0), name);
        await connected.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(SingleInstance.SendFailure.Timeout, await send);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(3), timer.Elapsed.ToString());
    }
}
