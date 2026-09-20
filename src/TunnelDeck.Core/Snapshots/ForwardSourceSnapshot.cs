namespace TunnelDeck.Core.Snapshots;

public sealed record ForwardSourceSnapshot
{
    public string ServerName { get; init; } = string.Empty;
    public Guid? ProfileId { get; init; }
    public string? ProfileName { get; init; }
    public Guid? RuleId { get; init; }
}
