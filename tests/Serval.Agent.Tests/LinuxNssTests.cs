using Serval.Agent;
using Xunit;

namespace Serval.Agent.Tests;

public sealed class LinuxNssTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "Requires Linux NSS.", SkipUnless = nameof(IsLinux))]
    public void ResolvesCurrentRootIdentityAndPrimaryGroup()
    {
        var account = LinuxNss.ResolveAccount("root");
        var group = LinuxNss.ResolveGroup("root");

        Assert.NotNull(account);
        Assert.Equal((uint)0, account.Uid);
        Assert.Equal((uint)0, account.PrimaryGid);
        Assert.Contains(account.Groups, item => item.Name == "root" && item.Gid == 0);
        Assert.Equal(new LinuxGroup("root", 0), group);
    }

    [Fact(Skip = "Requires Linux NSS.", SkipUnless = nameof(IsLinux))]
    public void RejectsMissingOrMalformedNames()
    {
        Assert.Null(LinuxNss.ResolveAccount("../root"));
        Assert.Null(LinuxNss.ResolveAccount("serval-user-that-does-not-exist"));
        Assert.Null(LinuxNss.ResolveGroup("../root"));
        Assert.Null(LinuxNss.ResolveGroup("serval-group-that-does-not-exist"));
    }

    [Fact(Skip = "Requires Linux NSS.", SkipUnless = nameof(IsLinux))]
    public void ResolvesNonRootUidAndRejectsRootPeerIdentity()
    {
        Assert.Null(LinuxNss.ResolveAccountByUid(0));
        var account = LinuxNss.ResolveAccount(Environment.UserName);
        Assert.NotNull(account);
        if (account.Uid != 0)
        {
            var byUid = LinuxNss.ResolveAccountByUid(account.Uid);
            Assert.NotNull(byUid);
            Assert.Equal(account.Name, byUid.Name);
            Assert.Equal(account.Uid, byUid.Uid);
            Assert.Equal(account.PrimaryGid, byUid.PrimaryGid);
        }
    }
}
