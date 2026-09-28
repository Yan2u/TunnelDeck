using System.Diagnostics;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ResourceCycleTests(SshFixture fixture)
{
    private const int Cycles = 100;
    private const int WarmupCycles = 10;

    [Fact]
    public async Task OneHundredRealStartStopCyclesReleaseListenersConnectionsAndProcessResources()
    {
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        int remotePort = await fixture.UnusedPortAsync();
        byte[] payload = [3, 1, 4, 1, 5, 9, 2, 6];
        var elapsed = Stopwatch.StartNew();
        ProcessSample? baseline = null;
        for (int cycle = 1; cycle <= Cycles; cycle++)
        {
            var starts = await scope.Tunnels.StartAllAsync([
                scope.Rule(ports.First, fixture.Manifest.EchoPort),
                scope.Rule(echo.Port, remotePort, ForwardMode.Remote),
            ]);
            Assert.All(starts, result => Assert.True(result.Succeeded, $"Cycle {cycle}: {result.Failure?.Message ?? result.Result?.Tunnel.LastError?.Message}"));
            Assert.Equal(2, scope.Connections.Manager.TrackedConnectionCount);
            await LocalEchoServer.AssertPayloadAsync(ports.First, payload);
            if (cycle % 10 == 0) { await fixture.AssertRemotePayloadAsync(remotePort, payload); }

            var stops = await scope.Tunnels.StopAllAsync();
            Assert.All(stops, result => Assert.True(result.Succeeded, $"Cycle {cycle}: {result.Failure?.Message}"));
            foreach (ActiveTunnel tunnel in scope.Tunnels.Current.Tunnels) { Assert.True(await scope.Tunnels.RemoveAsync(tunnel.Id)); }
            Assert.Empty(scope.Tunnels.Current.Tunnels);
            Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
            Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
            LocalPortBlock.AssertReusable(ports.First);
            if (cycle % 10 == 0) { await fixture.ExpectListeningAsync(remotePort, false); }
            if (cycle == WarmupCycles) { baseline = ProcessSample.Capture(); }
        }
        ProcessSample final = ProcessSample.Capture();
        // Each connection performs a rejected discovery handshake before its authenticated session, so opens exceed sessions.
        Assert.Equal(Cycles * 2, scope.Connections.Factory.Sessions.Count);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
        LifecycleEvidence.Write("cycles.json", new
        {
            cycles = Cycles, warmupCycles = WarmupCycles, elapsedMs = elapsed.ElapsedMilliseconds,
            handshakes = scope.Connections.Factory.Opens, sessions = scope.Connections.Factory.Sessions.Count, sessionsDisposed = scope.Connections.Factory.Sessions.Count(session => session.IsDisposed), baseline, final,
            handleGrowth = final.Handles - baseline!.Handles, threadGrowth = final.Threads - baseline.Threads,
            workingSetGrowthBytes = final.WorkingSet - baseline.WorkingSet,
            note = "Each cycle opens one shared Local transport and one independent Remote transport, transfers a payload, then stops and removes both.",
        });
        // Growth after the warm-up window is bounded; a steady per-cycle leak of even one handle or thread would exceed these limits.
        Assert.InRange(final.Handles - baseline.Handles, int.MinValue, 60);
        Assert.InRange(final.Threads - baseline.Threads, int.MinValue, 12);
        Assert.InRange(final.WorkingSet - baseline.WorkingSet, long.MinValue, 48L * 1024 * 1024);
    }

    private sealed record ProcessSample(int Handles, int Threads, long WorkingSet, long PrivateBytes, long ManagedBytes)
    {
        public static ProcessSample Capture()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new(process.HandleCount, process.Threads.Count, process.WorkingSet64, process.PrivateMemorySize64, GC.GetTotalMemory(false));
        }
    }
}
