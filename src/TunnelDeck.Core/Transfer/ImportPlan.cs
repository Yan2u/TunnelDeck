using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Transfer;

public enum ImportSource { TunnelDeckJson, SshConfig }

/// <summary>One document entry and the decision made for it. Unusable entries are listed, never silently dropped.</summary>
public sealed record ImportItem(ResourceKind Kind, string Name, string Description, bool Usable, ImmutableArray<string> Notes)
{
    public string AuthenticationNote { get; init; } = string.Empty;
    public bool HasNotes => !Notes.IsEmpty;
    public string NotesText => string.Join("\n", Notes.Select(note => UiText.Translate(note)));
    public string DisplayDescription => UiText.Translate(Description);
    public string Decision => Usable ? UiText.Get(Kind == ResourceKind.Server ? "Import_NewServer" : "Import_NewProfile") : UiText.Get("Diagnostic_ImportPlan_Skipped");
}

/// <summary>Everything the import will create, with fresh IDs already assigned and all internal references remapped.</summary>
public sealed record ImportPlan
{
    public required ImportSource Source { get; init; }
    public required long BaseRevision { get; init; }
    public required ImmutableArray<RemoteServer> Servers { get; init; }
    public required ImmutableArray<ForwardProfile> Profiles { get; init; }
    public required ResourceOrder Order { get; init; }
    public required ImmutableArray<ImportItem> Items { get; init; }
    public required ImmutableArray<string> Warnings { get; init; }
    public int UsableCount => Servers.Length + Profiles.Length;
    public int SkippedCount => Items.Count(item => !item.Usable);
    public bool CanCommit => UsableCount > 0;
    public bool HasWarnings => !Warnings.IsEmpty || Items.Any(item => item.HasNotes || !item.Usable);
}

public sealed class ImportDocumentException(string message) : Exception(message);
