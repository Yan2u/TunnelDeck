using System.Collections.Immutable;
using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Transfer;

/// <summary>Imports supported static connection fields and reports ignored options. Never executes SSH config.</summary>
public static partial class SshConfigParser
{
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    { "hostname", "user", "port", "identityfile" };

    public static ImportPlan Plan(string text, AppState current, string homeDirectory, Func<string, bool> keyFileExists)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(keyFileExists);
        if (text.Length > 8 * 1024 * 1024 || text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParser_TheSSHConfigIsTooLargeOrContains")); }
        ParsedConfig parsed = Parse(text);
        var items = ImmutableArray.CreateBuilder<ImportItem>();
        var servers = ImmutableArray.CreateBuilder<RemoteServer>();
        foreach ((string alias, int line) in parsed.Aliases)
        {
            var values = new Dictionary<string, Directive>(StringComparer.OrdinalIgnoreCase);
            var problems = new List<string>();
            var notes = new List<string>();
            string authenticationNote = string.Empty;
            var identities = new List<Directive>();
            foreach (HostBlock block in parsed.Blocks.Where(block => Matches(block.Patterns, alias)))
            {
                foreach (Directive directive in block.Directives)
                {
                    if (!Fields.Contains(directive.Keyword))
                    { notes.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineIsNotSupportedAndWasIgnored", directive.Line, directive.Keyword)); continue; }
                    if (directive.Keyword.Equals("identityfile", StringComparison.OrdinalIgnoreCase))
                    { identities.Add(directive); continue; }
                    // Preserve first-value semantics even when the first required value is malformed.
                    if (!values.TryAdd(directive.Keyword, directive)) { continue; }
                    if (directive.Error is not null) { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_Line", directive.Line, directive.Keyword, directive.Error)); continue; }
                    if (directive.Arguments.Length != 1)
                    { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineNeedsOneStaticValue", directive.Line, directive.Keyword)); continue; }
                }
            }
            string host = Value("hostname") ?? alias;
            string user = Value("user") ?? string.Empty;
            int port = 22;
            if (Value("port") is string portValue &&
                (!int.TryParse(portValue, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_LinePortMustBeBetween1And65535", values["port"].Line)); }
            if (user.Length == 0) { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineAddAnExplicitUserBeforeImportingThis", line)); }
            string? identityPath = ReadIdentity(identities, homeDirectory, keyFileExists, notes);
            AuthenticationMethod authentication = identityPath is null ? AuthenticationMethod.Password : AuthenticationMethod.PrivateKey;
            if (identities.Count == 0)
            {
                ServerAuthenticationDefaults defaults = ServerDefaultsResolver.Authentication(current.Settings, homeDirectory, keyFileExists);
                authentication = defaults.Method;
                identityPath = defaults.IdentityFilePath;
                if (authentication == AuthenticationMethod.PrivateKey)
                {
                    if (identityPath is null || !keyFileExists(identityPath))
                    { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_TheDefaultPrivateKeyFileIsMissingChoose")); }
                    else { AddAuthenticationNote(UiText.Invariant("Diagnostic_SshConfigParser_NoIdentityFileIsSpecifiedUsingTheDefaultPrivate", identityPath)); }
                }
            }
            var server = new RemoteServer
            {
                Id = Guid.NewGuid(), DisplayName = alias, Host = host, Username = user, Port = port,
                AuthMethod = authentication,
                IdentityFilePath = identityPath,
            };
            if (HasExpansion(host))
            { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineHostNameTokenOrEnvironmentExpansionIsNot", values.GetValueOrDefault("hostname")?.Line ?? line)); }
            if (HasExpansion(user))
            { problems.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineUserTokenOrEnvironmentExpansionIsNot", values.GetValueOrDefault("user")?.Line ?? line)); }
            ValidationResult validation = ConfigurationValidator.ValidateServer(server);
            if (!validation.IsValid)
            { problems.AddRange(validation.Issues.Select(issue => UiText.Invariant("Diagnostic_SshConfigParser_Line_472", line, issue.Message))); }
            notes.InsertRange(0, problems.Distinct());
            bool usable = problems.Count == 0;
            if (usable)
            {
                servers.Add(server);
                if (authentication == AuthenticationMethod.Password)
                { AddAuthenticationNote(identities.Count == 0
                    ? UiText.Invariant("Diagnostic_SshConfigParser_NoIdentityFileIsSpecifiedUsingTheDefaultPassword")
                    : UiText.Invariant("Diagnostic_SshConfigParser_ImportedWithPasswordAuthenticationAfterTheExplicitIdentityFile")); }
                else if (identities.Count > 0)
                { AddAuthenticationNote(UiText.Invariant("Diagnostic_SshConfigParser_UsingPrivateKeyAuthenticationFromIdentityFile", identityPath)); }
                if (current.Servers.Any(existing => existing.DisplayName == alias ||
                    existing.Host.Equals(host, StringComparison.OrdinalIgnoreCase) && existing.Port == port && existing.Username == user))
                { notes.Add(UiText.Invariant("Diagnostic_SshConfigParser_AMatchingNameOrEndpointAlreadyExistsA")); }
            }
            items.Add(new(ResourceKind.Server, alias, UiText.Invariant("Diagnostic_SshConfigParser_Line_477", ImportPlanner.Endpoint(server), line), usable, [.. notes])
            { AuthenticationNote = authenticationNote });

            void AddAuthenticationNote(string note) { authenticationNote = note; notes.Add(note); }

            string? Value(string name) => values.TryGetValue(name, out Directive? value) && value.Error is null && value.Arguments.Length == 1
                ? value.Arguments[0] : null;
        }
        var warnings = parsed.Warnings.ToList();
        if (parsed.Aliases.Count == 0) { warnings.Add(UiText.Invariant("Diagnostic_SshConfigParser_NoConcreteHostAliasesWereFoundHostSupplies")); }
        int skipped = items.Count(item => !item.Usable);
        if (skipped > 0) { warnings.Add(UiText.Invariant(skipped == 1 ? "Diagnostic_ImportSkippedOne" : "Diagnostic_ImportSkippedOther", skipped)); }
        if (items.Any(item => item.Notes.Any(note => note.Contains("ignored", StringComparison.OrdinalIgnoreCase))))
        { warnings.Add(UiText.Invariant("Diagnostic_SshConfigParser_SomeSSHOptionsWereIgnoredReviewEachEntry")); }
        return new()
        {
            Source = ImportSource.SshConfig, BaseRevision = current.Revision, Servers = servers.ToImmutable(),
            Profiles = [], Order = new() { OtherServerIds = [.. servers.Select(server => server.Id)] },
            Items = items.ToImmutable(), Warnings = [.. warnings.Distinct()],
        };
    }

    private static bool HasExpansion(string value) => value.Contains('%') || value.Contains("$" + "{", StringComparison.Ordinal);

    private static string? ReadIdentity(List<Directive> identities, string homeDirectory, Func<string, bool> keyFileExists, List<string> notes)
    {
        if (identities.Count == 0) { return null; }
        if (identities.Count > 1)
        {
            notes.Add(UiText.Invariant("Diagnostic_SshConfigParser_LinesMultipleIdentityFileValuesWereIgnoredChooseOne", string.Join(", ", identities.Select(identity => identity.Line))));
            return null;
        }
        Directive field = identities[0];
        try
        {
            if (field.Error is not null || field.Arguments.Length != 1)
            { throw new ImportDocumentException(field.Error ?? UiText.Invariant("Diagnostic_SshConfigParser_IdentityFileNeedsOneStaticValue")); }
            string path = ResolveIdentity(field.Arguments[0], homeDirectory);
            if (!keyFileExists(path)) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParser_ThePrivateKeyFileIsMissingOnThis")); }
            return path;
        }
        catch (ImportDocumentException exception)
        {
            notes.Add(UiText.Invariant("Diagnostic_SshConfigParser_LineIdentityFileWasIgnoredSetAuthenticationInThe", field.Line, exception.Message));
            return null;
        }
    }

    private static string ResolveIdentity(string value, string homeDirectory)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || HasExpansion(value))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParser_IdentityFileNeedsOneExplicitFilePathNoneTokens")); }
        if (value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith(@"~\", StringComparison.Ordinal))
        { value = Path.Combine(homeDirectory, value[2..]); }
        if (!Path.IsPathFullyQualified(value))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParser_IdentityFileNeedsAnAbsolutePathOrRelativeTo")); }
        try { return Path.GetFullPath(value); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParser_IdentityFileIsNotAUsableFilePath")); }
    }
}
