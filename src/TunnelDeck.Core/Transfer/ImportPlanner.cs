using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Transfer;

/// <summary>Validate the whole portable document before assigning any new IDs. No persistence or connection work.</summary>
public static class ImportPlanner
{
    public const int MaximumResources = 5000;
    public const int MaximumRules = 50000;

    public static ImportPlan Plan(TunnelDeckExport document, AppState current, Func<string, bool> keyFileExists)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(keyFileExists);
        AppState portable = Validate(document);
        var serverMap = portable.Servers.ToDictionary(server => server.Id, _ => Guid.NewGuid());
        var profileMap = portable.Profiles.ToDictionary(profile => profile.Id, _ => Guid.NewGuid());
        var items = ImmutableArray.CreateBuilder<ImportItem>();
        var servers = ImmutableArray.CreateBuilder<RemoteServer>();
        foreach (RemoteServer source in portable.Servers)
        {
            RequireKeyFile(source, keyFileExists);
            RemoteServer server = source with { Id = serverMap[source.Id] };
            var notes = ImmutableArray.CreateBuilder<string>();
            if (current.Servers.Any(existing => SameEndpoint(existing, server)))
            { notes.Add(UiText.Invariant("Diagnostic_ImportPlanner_ThisEndpointAlreadyExistsASeparateServerWill")); }
            else if (current.Servers.Any(existing => string.Equals(existing.DisplayName, server.DisplayName, StringComparison.OrdinalIgnoreCase) && server.DisplayName is not null))
            { notes.Add(UiText.Invariant("Diagnostic_ImportPlanner_ThisNameAlreadyExistsExistingServersWillKeep")); }
            servers.Add(server);
            items.Add(new(ResourceKind.Server, ServerLabel(server), Endpoint(server), true, notes.ToImmutable()));
        }
        var profiles = ImmutableArray.CreateBuilder<ForwardProfile>();
        foreach (ForwardProfile source in portable.Profiles)
        {
            ForwardProfile profile = source with
            {
                Id = profileMap[source.Id],
                Rules = [.. source.Rules.Select(rule => rule with { Id = Guid.NewGuid(), ServerId = serverMap[rule.ServerId] })],
            };
            profiles.Add(profile);
            ImmutableArray<string> notes = current.Profiles.Any(existing => StringComparer.OrdinalIgnoreCase.Equals(existing.Name, source.Name))
                ? [UiText.Invariant("Diagnostic_ImportPlanner_ThisNameAlreadyExistsASeparateProfileWill")] : [];
            items.Add(new(ResourceKind.Profile, source.Name, UiText.Invariant("Import_ProfileDescription",
                UiText.Count(source.Rules.Length, "rule", System.Globalization.CultureInfo.GetCultureInfo(UiText.English))), true, notes));
        }
        return new()
        {
            Source = ImportSource.TunnelDeckJson, BaseRevision = current.Revision,
            Servers = servers.ToImmutable(), Profiles = profiles.ToImmutable(), Items = items.ToImmutable(), Warnings = [],
            Order = new()
            {
                PinnedServerIds = [.. portable.Order.PinnedServerIds.Select(id => serverMap[id])],
                OtherServerIds = [.. portable.Order.OtherServerIds.Select(id => serverMap[id])],
                PinnedProfileIds = [.. portable.Order.PinnedProfileIds.Select(id => profileMap[id])],
                OtherProfileIds = [.. portable.Order.OtherProfileIds.Select(id => profileMap[id])],
            },
        };
    }

    public static AppState Validate(TunnelDeckExport document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Type != TunnelDeckExport.Kind) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_ChooseATunnelDeckExportNotAnApplicationState")); }
        if (document.SchemaVersion != TunnelDeckExport.CurrentSchemaVersion)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_ThisExportVersionIsNotSupportedUseA")); }
        if (document.Servers.IsDefault || document.Profiles.IsDefault || document.Order is null)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_TheExportMustIncludeServersProfilesAndTheir")); }
        if (document.Servers.Length + document.Profiles.Length > MaximumResources ||
            document.Profiles.Sum(profile => profile is null || profile.Rules.IsDefault ? 0L : profile.Rules.Length) > MaximumRules)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_ThisExportIsTooLargeExportFewerResources")); }
        if (document.Servers.Any(server => server is null) || document.Profiles.Any(profile => profile is null || profile.Rules.IsDefault || profile.Rules.Any(rule => rule is null)))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_TheExportContainsAnEmptyServerProfileOr")); }
        AppState portable = new()
        {
            Servers = [.. document.Servers.Select(server => server.ToServer(server.Id))],
            Profiles = [.. document.Profiles.Select(profile => new ForwardProfile
            {
                Id = profile.Id, Name = profile.Name, Rules = [.. profile.Rules.Select(rule => rule.ToRule(rule.Id, rule.ServerId))],
            })],
            Order = document.Order,
        };
        ValidationResult validation = AppStateValidator.Validate(portable);
        if (!validation.IsValid)
        {
            throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_NothingWasImportedFixTheseFieldsInThe") +
                string.Join("\n", validation.Issues.Take(20).Select(issue => $"{issue.Path}: {issue.Message}")));
        }
        Guid[] ids = [.. portable.Servers.Select(server => server.Id), .. portable.Profiles.Select(profile => profile.Id),
            .. portable.Profiles.SelectMany(profile => profile.Rules).Select(rule => rule.Id)];
        if (ids.Length != ids.Distinct().Count())
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_ResourceAndRuleIDsMustBeUniqueWithin")); }
        return portable;
    }

    internal static void RequireKeyFile(RemoteServer server, Func<string, bool> keyFileExists)
    {
        if (server.AuthMethod == AuthenticationMethod.PrivateKey &&
            (!Path.IsPathFullyQualified(server.IdentityFilePath!) || !keyFileExists(server.IdentityFilePath!)))
        {
            throw new ImportDocumentException(UiText.Invariant("Diagnostic_ImportPlanner_ThePrivateKeyPathMustPointToAn", ServerLabel(server)));
        }
    }

    internal static string ServerLabel(RemoteServer server) => string.IsNullOrWhiteSpace(server.DisplayName) ? Endpoint(server) : server.DisplayName;
    internal static string Endpoint(RemoteServer server) => $"{server.Username}@{server.Host}:{server.Port}";
    private static bool SameEndpoint(RemoteServer left, RemoteServer right) => left.Port == right.Port &&
        StringComparer.Ordinal.Equals(left.Username, right.Username) &&
        StringComparer.OrdinalIgnoreCase.Equals(left.Host.TrimEnd('.'), right.Host.TrimEnd('.'));
}
