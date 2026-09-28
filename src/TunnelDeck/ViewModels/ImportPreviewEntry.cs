using TunnelDeck.Core.Transfer;

namespace TunnelDeck.ViewModels;

public sealed class ImportPreviewEntry : ObservableObject
{
    private bool _expanded;

    public ImportItem Item { get; }
    public IReadOnlyList<ImportPreviewNote> Notes { get; }
    public string Footer => UiText.Translate(Item.AuthenticationNote);
    public bool HasDetails => Notes.Count > 0 || Footer.Length > 0;
    public string DecisionGlyph => Item.Usable ? "\uE73E" : "\uE7BA";
    public string AutomationId { get; }
    public string AccessibleName => $"{Item.Name}. {Item.DisplayDescription}. {Item.Decision}.";
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }

    public ImportPreviewEntry(ImportItem item, int index)
    {
        Item = item;
        AutomationId = $"ImportEntry_{index}";
        _expanded = !item.Usable;
        Notes = item.Notes.Where(note => note != item.AuthenticationNote).Select(ImportPreviewNote.FromText).ToArray();
    }
}

public sealed class ImportPreviewNote : ObservableObject
{
    private readonly string _text;
    private readonly string? _lines;
    private readonly bool _multipleLines;

    private ImportPreviewNote(string text)
    {
        _text = text;
        int separator = text.IndexOf(':');
        _multipleLines = text.StartsWith("Lines ", StringComparison.Ordinal);
        if (separator > 0 && (_multipleLines || text.StartsWith("Line ", StringComparison.Ordinal)))
        { _lines = text[(_multipleLines ? 6 : 5)..separator]; }
    }

    public string Location => _lines is null ? UiText.Get("ImportPreviewEntry_Note")
        : UiText.Get(_multipleLines ? "Import_Lines" : "Import_Line", _lines);
    public string Message
    {
        get
        {
            string text = UiText.Translate(_text);
            if (_lines is null) { return text; }
            int separator = text.IndexOf(UiText.Language == UiText.SimplifiedChinese ? '\uFF1A' : ':');
            return separator < 0 ? text : text[(separator + 1)..].TrimStart();
        }
    }

    public static ImportPreviewNote FromText(string text) => new(text);
}
