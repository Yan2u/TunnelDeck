using System.Collections.Immutable;

namespace TunnelDeck.Core.Validation;

internal sealed class ValidationBuilder
{
    private readonly ImmutableArray<ValidationIssue>.Builder _issues = ImmutableArray.CreateBuilder<ValidationIssue>();

    public void Require(bool condition, string path, string message)
    {
        if (!condition) { _issues.Add(new(path, message)); }
    }

    public void Add(string prefix, ValidationResult result)
    {
        foreach (ValidationIssue issue in result.Issues)
        {
            _issues.Add(issue with { Path = string.IsNullOrEmpty(prefix) ? issue.Path : $"{prefix}.{issue.Path}" });
        }
    }

    public void Id(Guid id, string path) => Require(id != Guid.Empty, path, UiText.Invariant("Diagnostic_ValidationBuilder_ANonEmptyIDIsRequired"));
    public void Port(int port, string path) => Require(PortNumber.IsValid(port), path, UiText.Invariant("Diagnostic_ValidationBuilder_EnterAWholeNumberPortFrom1To"));
    public void Utc(DateTimeOffset? value, string path) => Require(value is null || value.Value.Offset == TimeSpan.Zero, path, UiText.Invariant("Diagnostic_ValidationBuilder_StoreTimestampsInUTC"));
    public ValidationResult Build() => new(_issues.ToImmutable());
}
