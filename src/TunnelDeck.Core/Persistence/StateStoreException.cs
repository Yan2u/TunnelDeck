using System.Collections.Immutable;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Persistence;

public enum StateStoreFailure
{
    InvalidData,
    UnsupportedSchema,
    Unavailable,
    WriteFailed,
    RevisionConflict,
    ReadOnly,
    NotInitialized,
    ShuttingDown,
    CredentialWriteFailed,
}

/// <summary>Safe to display. Never includes JSON input, credential values or raw adapter exceptions.</summary>
public class StateStoreException(StateStoreFailure failure, string message,
    ImmutableArray<ValidationIssue> diagnostics = default) : Exception(message)
{
    public StateStoreFailure Failure { get; } = failure;
    public ImmutableArray<ValidationIssue> Diagnostics { get; } = diagnostics.IsDefault ? [] : diagnostics;
}

public sealed class StateRevisionConflictException(long expectedRevision, long actualRevision)
    : StateStoreException(StateStoreFailure.RevisionConflict, "Saved data changed. Reload it before saving this edit.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}
