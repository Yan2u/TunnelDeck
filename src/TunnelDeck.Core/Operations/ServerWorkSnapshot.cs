using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Operations;

public enum ServerWorkKind { Forward, Profile, ConnectionTest, Reconnect }

public sealed record PendingForward(RuleLaunchSnapshot Launch, Guid? TunnelId = null);

public sealed record ServerWorkSnapshot(Guid Id, RemoteServer Server, ServerWorkKind Kind,
    ImmutableArray<PendingForward> Forwards);

public sealed record ServerWorkRegistrySnapshot(long Revision, ImmutableArray<ServerWorkSnapshot> Work);
