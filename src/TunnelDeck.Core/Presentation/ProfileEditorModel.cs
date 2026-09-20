using System.Collections.ObjectModel;
using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Presentation;

/// <summary>A page-local draft. Rule edits and moves have no persistence or runtime side effects.</summary>
public sealed class ProfileEditorModel : ObservableModel
{
    private AppState _state;
    private ForwardProfile? _original;
    private string _name, _error = string.Empty;
    private bool _editing, _available = true;
    private Guid? _editingRule;
    public Guid Id { get; }
    public ForwardProfile? Original => _original;
    public ObservableCollection<ProfileRuleItem> Rules { get; } = [];
    public RuleEditorModel RuleEditor { get; } = new();
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) { Error = string.Empty; DraftChanged(); } } }
    public string Title => _original?.Name ?? "New profile";
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsEmpty => Rules.Count == 0;
    public bool IsEditingRule => _editing;
    public string RuleEditorTitle => _editingRule is null ? "Add forwarding rule" : "Edit forwarding rule";
    public bool CanEdit => _available && !IsEditingRule;
    public bool CanApplyRule => _available && IsEditingRule;
    public bool HasChanges => !ProfileEditPolicy.ContentEquals(_original, Candidate());
    public bool CanSave => _available && !IsEditingRule && HasChanges;
    public bool CanRunSaved => _available && !IsEditingRule && !HasChanges && !IsEmpty && _state.Profiles.Any(profile => profile.Id == Id);
    public string DraftHint => IsEditingRule ? "Apply or cancel this rule, then save the profile." : HasChanges ? "Save this profile before starting its rules. Running forwards keep their original settings." : string.Empty;

    public ProfileEditorModel(AppState state, Guid? id)
    {
        _state = state;
        _original = state.Profiles.FirstOrDefault(profile => profile.Id == id);
        Id = _original?.Id ?? Guid.NewGuid();
        _name = _original?.Name ?? string.Empty;
        foreach (ForwardRule rule in _original?.Rules ?? []) { Rules.Add(new(rule)); }
        Rules.CollectionChanged += (_, _) => DraftChanged();
    }

    public void UpdateState(AppState state) { _state = state; DraftChanged(); }
    public void SetAvailability(bool available) { _available = available; DraftChanged(); }

    public void AddRule(Guid? quickServerId)
    {
        if (!CanEdit) { return; }
        _editingRule = null;
        RuleEditor.Load(RuleDefaultsResolver.CreateRuleDraft(_state.Servers.Select(server => server.Id).ToHashSet(), _state.Settings, Candidate(), quickServerId));
        _editing = true; Error = string.Empty; DraftChanged();
    }

    public void EditRule(Guid id)
    {
        if (!CanEdit || Rules.FirstOrDefault(item => item.Id == id) is not { } item) { return; }
        _editingRule = id; RuleEditor.Load(ForwardRuleDraft.FromRule(item.Value)); _editing = true; Error = string.Empty; DraftChanged();
    }

    public bool ApplyRule()
    {
        if (!CanApplyRule || RuleEditor.Capture(_state) is not { } rule) { return false; }
        ProfileRuleItem? existing = Rules.FirstOrDefault(item => item.Id == _editingRule);
        if (existing is null) { Rules.Add(new(rule)); }
        else { Rules[Rules.IndexOf(existing)] = new(rule); }
        _editing = false; _editingRule = null; DraftChanged(); return true;
    }

    public void CancelRule() { _editing = false; _editingRule = null; DraftChanged(); }
    public void RemoveRule(Guid id)
    {
        if (CanEdit && Rules.FirstOrDefault(item => item.Id == id) is { } item) { Rules.Remove(item); }
    }
    public void MoveRule(Guid id, int delta)
    {
        if (!CanEdit || Rules.FirstOrDefault(item => item.Id == id) is not { } item) { return; }
        int from = Rules.IndexOf(item), to = from + delta;
        if (to >= 0 && to < Rules.Count) { Rules.Move(from, to); }
    }

    public ForwardProfile? Capture()
    {
        if (IsEditingRule) { Error = "Apply or cancel the rule before saving."; return null; }
        ForwardProfile candidate = Candidate();
        ValidationResult validation = ConfigurationValidator.ValidateProfile(candidate, _state.Servers.Select(server => server.Id).ToHashSet());
        Error = string.Join(" ", validation.Issues.Select(issue => issue.Message));
        return validation.IsValid ? candidate : null;
    }

    public void Saved(ForwardProfile profile) { _original = profile; _name = profile.Name; Error = string.Empty; OnPropertyChanged(nameof(Name)); DraftChanged(); }
    public void Discard()
    {
        _name = _original?.Name ?? string.Empty; _editing = false; _editingRule = null;
        Rules.Clear(); foreach (ForwardRule rule in _original?.Rules ?? []) { Rules.Add(new(rule)); }
        Error = string.Empty; OnPropertyChanged(nameof(Name)); DraftChanged();
    }
    private ForwardProfile Candidate() => new() { Id = Id, Name = Name.Trim(), Rules = [.. Rules.Select(item => item.Value)], LastUsedAt = _original?.LastUsedAt };
    private void DraftChanged()
    {
        foreach (string name in new[] { nameof(Title), nameof(IsEmpty), nameof(IsEditingRule), nameof(RuleEditorTitle), nameof(CanEdit), nameof(CanApplyRule), nameof(HasChanges), nameof(CanSave), nameof(CanRunSaved), nameof(DraftHint) }) { OnPropertyChanged(name); }
    }
}
