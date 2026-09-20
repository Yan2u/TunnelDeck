using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ProfileEditorTests
{
    [Fact]
    public void NewRulesInheritThePreviousDraftRuleServerAndKeepTheirOwnIdentity()
    {
        var state = DomainData.State([DomainData.Server(), DomainData.Server(2)], []);
        var editor = new ProfileEditorModel(state, null) { Name = "Cross server" };
        editor.AddRule(DomainData.Id(2));
        Assert.Equal(DomainData.Id(2), editor.RuleEditor.SelectedServerId);
        editor.RuleEditor.LocalPortText = "3000"; editor.RuleEditor.RemotePortText = "5432";
        Assert.True(editor.ApplyRule());
        Guid id = editor.Rules[0].Id;
        editor.EditRule(id); editor.RuleEditor.ModeIndex = 1; editor.RuleEditor.AutoFindPort = true;
        Assert.True(editor.ApplyRule());
        Assert.Equal(id, editor.Rules[0].Id);
        Assert.False(editor.Rules[0].Value.AutoFindAvailablePort);
        editor.AddRule(DomainData.Id(1));
        Assert.Equal(DomainData.Id(2), editor.RuleEditor.SelectedServerId);
        Assert.Empty(editor.RuleEditor.LocalPortText);
        editor.RuleEditor.LocalPortText = "4000"; editor.RuleEditor.RemotePortText = "4001";
        Assert.True(editor.ApplyRule());
        Assert.NotEqual(id, editor.Rules[1].Id);
        Assert.Equal(2, editor.Capture()!.Rules.Length);
        Assert.Empty(state.Profiles);
    }

    [Theory]
    [InlineData("3.5")]
    [InlineData("1e3")]
    [InlineData("65536")]
    [InlineData("invalid")]
    public void InvalidPortTextDoesNotReplaceTheOriginalRule(string raw)
    {
        var profile = DomainData.Profile(1, DomainData.Rule());
        var editor = new ProfileEditorModel(DomainData.State([DomainData.Server()], [profile]), profile.Id);
        editor.EditRule(profile.Rules[0].Id);
        editor.RuleEditor.LocalPortText = raw;
        Assert.False(editor.ApplyRule());
        Assert.NotEmpty(editor.RuleEditor.LocalPortError);
        Assert.Equal(profile.Rules[0], editor.Rules[0].Value);
        Assert.False(editor.CanSave);
        Assert.Null(editor.Capture());
        editor.CancelRule();
        Assert.False(editor.HasChanges);
    }

    [Fact]
    public void RemovingMovingAndDiscardingOnlyAffectsTheDraft()
    {
        var profile = DomainData.Profile(1, DomainData.Rule(1), DomainData.Rule(2, localPort: 4000), DomainData.Rule(3, localPort: 5000));
        AppState state = DomainData.State([DomainData.Server()], [profile]);
        ProfileLaunchSnapshot launch = ProfileSnapshotFactory.Capture(profile, state.Servers);
        var editor = new ProfileEditorModel(state, profile.Id);
        editor.Name = "Changed";
        editor.MoveRule(profile.Rules[0].Id, 2);
        editor.RemoveRule(profile.Rules[1].Id);
        Assert.Equal(new[] { profile.Rules[2], profile.Rules[0] }, editor.Capture()!.Rules);
        Assert.Equal(profile.Rules, launch.Rules.Select(rule => rule.Requested));
        Assert.Equal(profile, state.Profiles.Single());
        Assert.False(editor.CanRunSaved);
        editor.Discard();
        Assert.Equal(profile.Rules, editor.Rules.Select(rule => rule.Value));
        Assert.Equal(profile.Name, editor.Name);
        Assert.True(editor.CanRunSaved);
    }

    [Fact]
    public void ConfigurationRefreshPreservesOpenRuleInputAndDoesNotApplyNewDefaults()
    {
        AppState state = DomainData.State([DomainData.Server(), DomainData.Server(2)], []);
        var editor = new ProfileEditorModel(state, null);
        editor.AddRule(DomainData.Id(2));
        editor.RuleEditor.LocalPortText = "5210";
        editor.UpdateState(state with { Revision = 100, Settings = state.Settings with { AutoFindAvailablePortDefault = false, DefaultServerId = DomainData.Id(1) } });
        Assert.Equal(DomainData.Id(2), editor.RuleEditor.SelectedServerId);
        Assert.Equal("5210", editor.RuleEditor.LocalPortText);
        Assert.True(editor.RuleEditor.AutoFindPort);
        editor.RuleEditor.LocalHost = "some-hostname";
        editor.RuleEditor.RemotePortText = "5210";
        Assert.False(editor.ApplyRule());
        Assert.NotEmpty(editor.RuleEditor.LocalHostError);
        editor.RuleEditor.ModeIndex = 1;
        Assert.True(editor.ApplyRule());
    }

    [Fact]
    public void OnlySavedUnchangedNonemptyProfilesCanStart()
    {
        var profile = DomainData.Profile(1, DomainData.Rule());
        var state = DomainData.State([DomainData.Server()], [profile]);
        var editor = new ProfileEditorModel(state, profile.Id);
        Assert.True(editor.CanRunSaved);
        editor.Name = "Renamed"; Assert.False(editor.CanRunSaved);
        editor.Saved(editor.Capture()!); Assert.True(editor.CanRunSaved);
        editor.SetAvailability(false); Assert.False(editor.CanRunSaved); Assert.False(editor.CanSave);
        editor.SetAvailability(true); editor.RemoveRule(profile.Rules[0].Id);
        Assert.False(editor.CanRunSaved);
        Assert.False(new ProfileEditorModel(state, null).CanRunSaved);
    }
}
