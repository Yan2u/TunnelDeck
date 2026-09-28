namespace TunnelDeck.Infrastructure.Ssh;

public sealed record SshConnectionOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan LibraryTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ControlTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    internal void Validate()
    {
        if (ConnectTimeout <= TimeSpan.Zero || LibraryTimeout < ConnectTimeout ||
            ControlTimeout <= TimeSpan.Zero || ControlTimeout >= LibraryTimeout || KeepAliveInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException(UiText.Invariant("Diagnostic_SshConnectionOptions_SSHDeadlinesMustBePositiveTheLibraryTimeout"));
        }
    }
}
