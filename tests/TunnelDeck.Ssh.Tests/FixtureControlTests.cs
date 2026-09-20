using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class FixtureControlTests(SshFixture fixture)
{
    [Fact]
    public async Task ControlRequestsRemainReliableAfterConcurrentDataProbes()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.AssertRemotePayloadAsync(fixture.Manifest.EchoPort, [1, 2, 0, 255])));
        for (int index = 0; index < 100; index++)
        {
            int port = await fixture.ReservePortAsync();
            try { Assert.True(await fixture.IsListeningAsync(port)); }
            finally { await fixture.ReleasePortAsync(port); }
        }
    }
}
