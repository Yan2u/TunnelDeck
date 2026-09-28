namespace TunnelDeck.Core.Abstractions;

public sealed class TerminalLaunchException(string message) : Exception(message);
