namespace TunnelDeck.Core.Abstractions;

public interface ICredentialStore
{
    string? Read(Guid reference);
    void Write(Guid reference, string secret);
    void Delete(Guid reference);
}
