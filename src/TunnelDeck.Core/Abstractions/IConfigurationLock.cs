namespace TunnelDeck.Core.Abstractions;

/// <summary>Serializes whole configuration/credential transactions, including across app instances.</summary>
public interface IConfigurationLock
{
    ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default);
}
