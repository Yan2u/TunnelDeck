using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Tests;

public sealed class HostKeyIdentityTests
{
    [Theory]
    [InlineData("EXAMPLE.COM.", "example.com")]
    [InlineData("BÜCHER.EXAMPLE。", "xn--bcher-kva.example")]
    [InlineData("2001:0db8:0:0:0:0:0:1", "2001:db8::1")]
    public void EquivalentHostSpellingsMatch(string first, string second)
    {
        HostKeyIdentity key = HostKeyIdentity.FromKey(first, 22, "ssh-ed25519", [1, 2, 3]);
        Assert.True(key.Matches(HostKeyIdentity.FromKey(second, 22, "ssh-ed25519", [1, 2, 3])));
    }

    [Fact]
    public void EndpointAlgorithmAndKeyAreAllPartOfTrust()
    {
        HostKeyIdentity key = HostKeyIdentity.FromKey("host", 22, "ssh-ed25519", [1, 2, 3]);
        Assert.False(key.Matches(key with { Host = "another-host" }));
        Assert.False(key.Matches(key with { Port = 2222 }));
        Assert.False(key.Matches(key with { Algorithm = "ssh-rsa" }));
        Assert.False(key.Matches(HostKeyIdentity.FromKey("host", 22, "ssh-ed25519", [3, 2, 1])));
        Assert.StartsWith("SHA256:", key.Fingerprint);
    }
}
