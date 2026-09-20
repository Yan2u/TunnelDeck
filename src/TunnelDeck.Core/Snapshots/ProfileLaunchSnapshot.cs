using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Snapshots;

public sealed record RuleLaunchSnapshot(RemoteServer Server, ForwardRule Requested, ForwardSourceSnapshot Source);

public sealed record ProfileLaunchSnapshot(Guid ProfileId, string ProfileName, DateTimeOffset CapturedAt,
    ImmutableArray<RuleLaunchSnapshot> Rules);
