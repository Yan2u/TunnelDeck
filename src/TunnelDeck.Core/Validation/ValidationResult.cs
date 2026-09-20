using System.Collections.Immutable;

namespace TunnelDeck.Core.Validation;

public sealed record ValidationIssue(string Path, string Message);

public sealed record ValidationResult(ImmutableArray<ValidationIssue> Issues)
{
    public bool IsValid => Issues.IsEmpty;

    public void ThrowIfInvalid()
    {
        if (!IsValid) { throw new DomainValidationException(this); }
    }
}

public sealed class DomainValidationException(ValidationResult result)
    : ArgumentException(string.Join(" ", result.Issues.Select(issue => $"{issue.Path}: {issue.Message}")))
{
    public ValidationResult Result { get; } = result;
}
