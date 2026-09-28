using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace TunnelDeck.Core.Localization;

public static partial class UiText
{
    private static readonly Lazy<DiagnosticCatalog> Diagnostics = new(CreateDiagnosticCatalog);

    /// <summary>
    /// Translate application-owned diagnostics at the display boundary. Persisted messages stay invariant,
    /// including activity written by earlier releases. Never pass user names, paths, secrets or SSH output here.
    /// </summary>
    public static string Translate(string? message) => Translate(message ?? string.Empty, 0);

    private static string Translate(string message, int depth)
    {
        if (message.Length == 0 || depth > 4) { return message; }
        DiagnosticCatalog catalog = Diagnostics.Value;
        if (catalog.Exact.TryGetValue(message, out string? key)) { return Get(key); }
        int fieldSeparator = message.IndexOf(": ", StringComparison.Ordinal);
        if (fieldSeparator > 0 && Regex.IsMatch(message[..fieldSeparator], @"\A[A-Za-z]+(?:\[\d+\])?(?:\.[A-Za-z]+(?:\[\d+\])?)*\z"))
        {
            string field = message[..fieldSeparator];
            string localized = Regex.Replace(field, "[A-Za-z]+", match => Resources.GetString("Field_" + match.Value, Culture) ?? match.Value);
            string detail = Translate(message[(fieldSeparator + 2)..], depth + 1);
            if (localized != field || detail != message[(fieldSeparator + 2)..])
            { return localized + (Language == SimplifiedChinese ? "\uFF1A" : ": ") + detail; }
        }
        foreach (DiagnosticTemplate template in catalog.Templates)
        {
            if (!message.StartsWith(template.Prefix, StringComparison.Ordinal)) { continue; }
            Match match = template.Pattern.Match(message);
            if (!match.Success) { continue; }
            object?[] arguments = new object?[template.ArgumentCount];
            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = match.Groups["a" + index].Value;
                arguments[index] = IsNestedDiagnostic(template.Key, index) ? Translate(argument, depth + 1) : argument;
            }
            return Get(template.Key, arguments);
        }
        // Joined validation and storage diagnostics are separate messages, not free-form replacement text.
        if (message.Contains('\n')) { return string.Join('\n', message.Split('\n').Select(line => Translate(line, depth + 1))); }
        foreach ((string source, string resourceKey) in catalog.Prefixes)
        {
            if (message.StartsWith(source, StringComparison.Ordinal) && message.Length > source.Length &&
                char.IsWhiteSpace(message[source.Length]))
            { return Get(resourceKey) + Translate(message[source.Length..], depth + 1); }
        }
        string trimmed = message.TrimStart();
        return trimmed.Length != message.Length ? message[..(message.Length - trimmed.Length)] + Translate(trimmed, depth + 1) : message;
    }

    private static bool IsNestedDiagnostic(string key, int argument) =>
        key == "Diagnostic_SshConfigParser_Line_472" && argument == 1 ||
        key == "Diagnostic_SshConfigParser_Line" && argument == 2 ||
        key == "Diagnostic_SshConfigParser_LineIdentityFileWasIgnoredSetAuthenticationInThe" && argument == 1 ||
        key is "Import_ProfileDescription" or "ExportViewModel_RequiredServersIncluded" && argument == 0 ||
        key.StartsWith("Diagnostic_ResourceDeletionService_", StringComparison.Ordinal) && argument == 0 ||
        key == "MainWindowStorage_DataFolder_182" && argument == 0;

    private static DiagnosticCatalog CreateDiagnosticCatalog()
    {
        var exact = new Dictionary<string, string>(StringComparer.Ordinal);
        var templates = new List<DiagnosticTemplate>();
        foreach (string language in new[] { English, SimplifiedChinese })
        {
            ResourceSet resources = Resources.GetResourceSet(CultureInfo.GetCultureInfo(language), true, true)!;
            foreach (DictionaryEntry entry in resources)
            {
                if (entry.Key is not string key || entry.Value is not string text || text.Length == 0) { continue; }
                MatchCollection placeholders = Regex.Matches(text, @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})");
                if (placeholders.Count == 0) { exact.TryAdd(text, key); continue; }
                var pattern = new StringBuilder("\\A");
                int position = 0, count = 0, literalLength = 0;
                foreach (Match placeholder in placeholders)
                {
                    string literal = text[position..placeholder.Index];
                    literalLength += literal.Length;
                    pattern.Append(Regex.Escape(literal));
                    int index = int.Parse(placeholder.Groups[1].Value, CultureInfo.InvariantCulture);
                    count = Math.Max(count, index + 1);
                    pattern.Append("(?<a").Append(index).Append(">.*?)");
                    position = placeholder.Index + placeholder.Length;
                }
                literalLength += text.Length - position;
                if (literalLength < 4) { continue; }
                pattern.Append(Regex.Escape(text[position..])).Append("\\z");
                templates.Add(new(key, text[..placeholders[0].Index], count, literalLength,
                    new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.NonBacktracking)));
            }
        }
        return new(exact, templates.OrderByDescending(template => template.LiteralLength).ToArray(),
            exact.Where(pair => pair.Key.Length >= 16 && pair.Key.EndsWith('.')).OrderByDescending(pair => pair.Key.Length).ToArray());
    }

    private sealed record DiagnosticTemplate(string Key, string Prefix, int ArgumentCount, int LiteralLength, Regex Pattern);
    private sealed record DiagnosticCatalog(Dictionary<string, string> Exact, DiagnosticTemplate[] Templates, KeyValuePair<string, string>[] Prefixes);
}
