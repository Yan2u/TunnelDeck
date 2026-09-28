using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TunnelDeck.Core.Localization;

namespace TunnelDeck.Core.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly ResourceManager Resources = new("TunnelDeck.Core.Localization.Strings", typeof(UiText).Assembly);

    [Fact]
    public void ChineseCatalogHasEveryEnglishKeyAndPreservesFormatArguments()
    {
        Dictionary<string, string> english = ReadCatalog(CultureInfo.InvariantCulture);
        Dictionary<string, string> chinese = ReadCatalog(CultureInfo.GetCultureInfo(UiText.SimplifiedChinese));
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), chinese.Keys.Order());
        foreach ((string key, string original) in english)
        {
            string translated = chinese[key];
            Assert.False(string.IsNullOrWhiteSpace(translated), key);
            Assert.Equal(CompositeFormat.Parse(original).MinimumArgumentCount, CompositeFormat.Parse(translated).MinimumArgumentCount);
            Assert.Equal(Arguments(original), Arguments(translated));
        }
    }

    [Fact]
    public void XamlLabelsPlaceholdersAndAccessibleNamesUseExistingResources()
    {
        Dictionary<string, string> catalog = ReadCatalog(CultureInfo.InvariantCulture);
        string[] paths = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "XamlSource"), "*.xaml", SearchOption.AllDirectories);
        Assert.NotEmpty(paths);
        HashSet<string> textProperties =
        [
            "Text", "Content", "Header", "Label", "Description", "PlaceholderText", "Title",
            "ToolTipService.ToolTip", "AutomationProperties.Name", "AutomationProperties.HelpText",
            "OnContent", "OffContent", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText",
        ];
        // Product names, file formats and example connection values are intentionally language independent.
        HashSet<string> literals = ["TunnelDeck", "JSON", "dev.example.com", @"C:\Keys\id_ed25519"];
        foreach (string path in paths)
        {
            XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (XAttribute attribute in document.Descendants().Attributes())
            {
                string value = attribute.Value;
                if (textProperties.Contains(attribute.Name.LocalName) && !value.StartsWith('{') && value.Any(char.IsLetter))
                {
                    Assert.True(literals.Contains(value), $"{Path.GetFileName(path)}: {attribute.Name} must use a resource: {value}");
                }
                foreach (Match match in Regex.Matches(value, @"Values\[([^\]]+)\]"))
                {
                    Assert.True(catalog.ContainsKey(match.Groups[1].Value), $"{path}: missing resource {match.Groups[1].Value}");
                }
            }
        }
    }

    private static Dictionary<string, string> ReadCatalog(CultureInfo culture) =>
        Resources.GetResourceSet(culture, true, false)!.Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

    private static string[] Arguments(string text) => Regex.Matches(text, @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
        .Select(match => match.Groups[1].Value).Distinct().Order().ToArray();
}
