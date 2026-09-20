using TunnelDeck.Infrastructure.Security;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void DisposableCredentialRoundTripsReplacesAndDeletes()
    {
        var store = new WindowsCredentialStore("TunnelDeck/Tests/" + Guid.NewGuid().ToString("N"));
        Guid reference = Guid.NewGuid();
        string first = "fixture-only-测试-" + Guid.NewGuid();
        string second = "fixture-only-replaced-" + Guid.NewGuid();
        try
        {
            Assert.Null(store.Read(reference));
            store.Write(reference, first);
            Assert.True(first == store.Read(reference), "Credential round-trip did not match.");
            store.Write(reference, second);
            Assert.True(second == store.Read(reference), "Credential replacement did not match.");
            store.Delete(reference);
            Assert.Null(store.Read(reference));
            store.Delete(reference);
        }
        finally
        {
            store.Delete(reference);
        }
    }
}
