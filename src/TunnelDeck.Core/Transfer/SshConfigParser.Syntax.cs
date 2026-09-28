using System.Text;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Transfer;

public static partial class SshConfigParser
{
    private sealed record Directive(int Line, string Keyword, string[] Arguments, string? Error = null);
    private sealed record HostBlock(int Line, string[] Patterns, List<Directive> Directives);
    private sealed record ParsedConfig(List<HostBlock> Blocks, List<(string Alias, int Line)> Aliases, List<string> Warnings);

    private static ParsedConfig Parse(string text)
    {
        var parsed = new ParsedConfig([], [], []);
        HostBlock current = new(0, ["*"], []);
        parsed.Blocks.Add(current);
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        int line = 0;
        bool insideMatch = false;
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is string source)
        {
            if (++line > 50000) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParserSyntax_TheSSHConfigHasTooManyLines")); }
            Directive? directive = Tokenize(source, line);
            if (directive is null) { continue; }
            if (directive.Keyword.Equals("host", StringComparison.OrdinalIgnoreCase))
            {
                insideMatch = false;
                if (directive.Error is not null || directive.Arguments.Length == 0)
                {
                    parsed.Warnings.Add(UiText.Invariant("Diagnostic_SshConfigParserSyntax_LineTheHostDeclarationCouldNotBeParsed", line));
                    current = new(line, [], []);
                    continue;
                }
                current = new(line, directive.Arguments, []);
                parsed.Blocks.Add(current);
                foreach (string pattern in directive.Arguments)
                {
                    if (IsConcreteAlias(pattern) && aliases.Add(pattern)) { parsed.Aliases.Add((pattern, line)); }
                    else if (pattern != "*" && !IsConcreteAlias(pattern))
                    { parsed.Warnings.Add(UiText.Invariant("Diagnostic_SshConfigParserSyntax_LineHostPatternDoesNotCreateAServer", line, pattern)); }
                }
                if (parsed.Aliases.Count > ImportPlanner.MaximumResources)
                { throw new ImportDocumentException(UiText.Invariant("Diagnostic_SshConfigParserSyntax_TheSSHConfigContainsTooManyServerAliases")); }
            }
            else if (directive.Keyword.Equals("match", StringComparison.OrdinalIgnoreCase))
            {
                insideMatch = true;
                parsed.Warnings.Add(UiText.Invariant("Diagnostic_SshConfigParserSyntax_LineMatchAndItsConditionalSettingsWereIgnored", line));
            }
            else if (insideMatch)
            { parsed.Warnings.Add(UiText.Invariant("Diagnostic_SshConfigParserSyntax_LineInsideMatchWasIgnored", line, directive.Keyword)); }
            else
            {
                current.Directives.Add(directive);
                if (directive.Keyword.Equals("include", StringComparison.OrdinalIgnoreCase))
                { parsed.Warnings.Add(UiText.Invariant("Diagnostic_SshConfigParserSyntax_LineIncludeWasIgnoredReferencedFilesWereNot", line)); }
            }
        }
        return parsed;
    }

    private static Directive? Tokenize(string source, int line)
    {
        string value = source.TrimStart();
        if (value.Length == 0 || value[0] == '#') { return null; }
        int separator = 0;
        while (separator < value.Length && !char.IsWhiteSpace(value[separator]) && value[separator] != '=') { separator++; }
        string keyword = value[..separator];
        value = value[separator..].TrimStart();
        if (value.StartsWith('=')) { value = value[1..].TrimStart(); }
        var arguments = new List<string>();
        var token = new StringBuilder();
        char quote = '\0';
        bool started = false;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is '"' or '\'')
            {
                if (quote == '\0') { quote = character; started = true; continue; }
                if (quote == character) { quote = '\0'; continue; }
            }
            if (character == '\\' && index + 1 < value.Length && value[index + 1] is '"' or '\'' or '\\')
            { token.Append(value[++index]); started = true; continue; }
            if (quote == '\0' && !started && character == '#') { break; }
            if (quote == '\0' && char.IsWhiteSpace(character))
            {
                if (started) { arguments.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(character); started = true; }
        }
        if (quote != '\0') { return new(line, keyword, [], UiText.Invariant("Diagnostic_SshConfigParserSyntax_AnOpeningQuoteHasNoClosingQuote")); }
        if (started) { arguments.Add(token.ToString()); }
        return new(line, keyword, [.. arguments]);
    }

    private static bool IsConcreteAlias(string pattern) => !pattern.StartsWith('-') &&
        !pattern.Any(character => character is '*' or '?' or '!') && HostAddress.IsValidHost(pattern);

    private static bool Matches(string[] patterns, string alias)
    {
        bool positive = false;
        foreach (string pattern in patterns)
        {
            bool negate = pattern.StartsWith('!');
            if (!MatchPattern(negate ? pattern[1..] : pattern, alias)) { continue; }
            if (negate) { return false; }
            positive = true;
        }
        return positive;
    }

    private static bool MatchPattern(string pattern, string value)
    {
        int position = 0, character = 0, star = -1, retry = 0;
        while (character < value.Length)
        {
            if (position < pattern.Length && (pattern[position] == '?' ||
                char.ToUpperInvariant(pattern[position]) == char.ToUpperInvariant(value[character]))) { position++; character++; }
            else if (position < pattern.Length && pattern[position] == '*') { star = position++; retry = character; }
            else if (star >= 0) { position = star + 1; character = ++retry; }
            else { return false; }
        }
        while (position < pattern.Length && pattern[position] == '*') { position++; }
        return position == pattern.Length;
    }
}
