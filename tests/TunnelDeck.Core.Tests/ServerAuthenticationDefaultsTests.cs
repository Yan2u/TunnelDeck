using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class ServerAuthenticationDefaultsTests
{
    private static readonly string UserDirectory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TunnelDeck.Defaults.Tests"));
    private static readonly string DefaultKey = Path.Combine(UserDirectory, "default key");
    private static readonly string ExplicitKey = Path.Combine(UserDirectory, "explicit key");
    private static readonly AppSettings KeySettings = new()
    { DefaultAuthenticationMethod = AuthenticationMethod.PrivateKey, DefaultIdentityFilePath = DefaultKey };

    [Fact]
    public void PasswordDefaultsDoNotProbeOrCopyARetainedPrivateKey()
    {
        ServerDraft draft = ServerDefaultsResolver.CreateDraft(KeySettings with { DefaultAuthenticationMethod = AuthenticationMethod.Password },
            UserDirectory, _ => throw new InvalidOperationException("Password defaults should not probe key files."));
        Assert.Equal(AuthenticationMethod.Password, draft.AuthMethod);
        Assert.Null(draft.IdentityFilePath);
        Assert.Null(draft.CredentialReference);
    }

    [Fact]
    public void PrivateKeyDefaultsDetectOnlyIdRsaAndKeepAManuallyConfiguredPath()
    {
        var probes = new List<string>();
        var automatic = KeySettings with { DefaultIdentityFilePath = null };
        ServerDraft detected = ServerDefaultsResolver.CreateDraft(automatic, UserDirectory, path => { probes.Add(path); return true; });
        Assert.Equal(Path.Combine(UserDirectory, ".ssh", "id_rsa"), Assert.Single(probes));
        Assert.Equal(probes[0], detected.IdentityFilePath);
        ServerDraft selected = ServerDefaultsResolver.CreateDraft(KeySettings, UserDirectory, _ => throw new InvalidOperationException());
        Assert.Equal(DefaultKey, selected.IdentityFilePath);
        Assert.Equal(AuthenticationMethod.PrivateKey, selected.AuthMethod);
    }

    [Fact]
    public void ChangedDefaultsDoNotRewriteAnExistingDraftOrSavedServer()
    {
        ServerDraft draft = ServerDefaultsResolver.CreateDraft(KeySettings, UserDirectory, _ => true);
        AppSettings changed = KeySettings with { DefaultAuthenticationMethod = AuthenticationMethod.Password };
        Assert.Equal(AuthenticationMethod.PrivateKey, draft.AuthMethod);
        Assert.Equal(DefaultKey, draft.IdentityFilePath);
        Assert.Equal(AuthenticationMethod.Password, ServerDefaultsResolver.CreateDraft(changed, UserDirectory, _ => true).AuthMethod);
        RemoteServer saved = DomainData.Server() with { AuthMethod = AuthenticationMethod.Password };
        Assert.Null(ServerDraft.FromServer(saved).IdentityFilePath);
        Assert.Equal(AuthenticationMethod.Password, saved.AuthMethod);
    }

    [Theory]
    [InlineData(AuthenticationMethod.Password)]
    [InlineData(AuthenticationMethod.PrivateKey)]
    public void AnExplicitIdentityFileOverridesEitherDefaultAuthenticationMethod(AuthenticationMethod defaultMethod)
    {
        ImportPlan plan = Plan($"IdentityFile \"{ExplicitKey}\"", KeySettings with { DefaultAuthenticationMethod = defaultMethod });
        RemoteServer server = Assert.Single(plan.Servers);
        Assert.Equal(AuthenticationMethod.PrivateKey, server.AuthMethod);
        Assert.Equal(ExplicitKey, server.IdentityFilePath);
        Assert.Contains("from IdentityFile", plan.Items[0].NotesText);
    }

    [Fact]
    public void AbsentIdentityFileUsesConfiguredDefaultsEvenWithOtherIgnoredOptions()
    {
        ImportPlan plan = Plan("Compression yes\n ForwardX11 yes", KeySettings);
        RemoteServer server = Assert.Single(plan.Servers);
        Assert.Equal(AuthenticationMethod.PrivateKey, server.AuthMethod);
        Assert.Equal(DefaultKey, server.IdentityFilePath);
        Assert.Contains("from Settings", plan.Items[0].NotesText);
        Assert.Contains("ignored", plan.Items[0].NotesText);
        Assert.Null(server.CredentialReference);
    }

    [Fact]
    public void NoDetectedKeyLeavesANewDraftEditableButDoesNotDowngradeAnImportToPassword()
    {
        AppSettings automatic = KeySettings with { DefaultIdentityFilePath = null };
        var draft = ServerDefaultsResolver.CreateDraft(automatic, UserDirectory, _ => false);
        Assert.Equal(AuthenticationMethod.PrivateKey, draft.AuthMethod);
        Assert.Null(draft.IdentityFilePath);
        ImportPlan plan = Plan(string.Empty, automatic, _ => false);
        Assert.False(plan.CanCommit);
        Assert.Contains("default private-key file is missing", plan.Items[0].NotesText);
    }

    [Theory]
    [InlineData("IdentityFile none")]
    [InlineData("IdentityFile missing-relative-key")]
    [InlineData("IdentityFile ~/.ssh/first\n IdentityFile ~/.ssh/second")]
    public void AnExplicitUnusableIdentityDoesNotSilentlySelectTheGlobalKey(string directive)
    {
        ImportPlan plan = Plan(directive, KeySettings);
        RemoteServer server = Assert.Single(plan.Servers);
        Assert.Equal(AuthenticationMethod.Password, server.AuthMethod);
        Assert.Null(server.IdentityFilePath);
        Assert.Contains("defaults were not substituted", plan.Items[0].NotesText);
    }

    [Fact]
    public void JsonImportsKeepTheirExplicitAuthenticationRatherThanApplyingDefaults()
    {
        AppState source = DomainData.State([DomainData.Server()], []);
        TunnelDeckExport document = ExportBuilder.Build(source, ExportScope.Servers, DomainData.Now);
        ImportPlan plan = ImportPlanner.Plan(document, new() { Settings = KeySettings }, _ => true);
        Assert.Equal(AuthenticationMethod.Password, Assert.Single(plan.Servers).AuthMethod);
        Assert.Null(plan.Servers[0].IdentityFilePath);
    }

    [Fact]
    public void UnknownDefaultAuthenticationAndRelativeSavedPathsAreRejected()
    {
        Assert.False(ConfigurationValidator.ValidateSettings(new() { DefaultAuthenticationMethod = (AuthenticationMethod)99 }).IsValid);
        Assert.False(ConfigurationValidator.ValidateSettings(KeySettings with { DefaultIdentityFilePath = "relative/key" }).IsValid);
        Assert.True(ConfigurationValidator.ValidateSettings(KeySettings with { DefaultIdentityFilePath = null }).IsValid);
    }

    [Fact]
    public async Task ChangedDefaultsInvalidateAnOldPreviewAndAMissingDefaultKeyPreventsAPartialCommit()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        await fixture.Configuration.UpdateSettingsAsync(_ => KeySettings);
        ImportPlan plan = SshConfigParser.Plan("Host imported\n User dev\n", fixture.Configuration.Current!, UserDirectory, _ => true);
        var unavailable = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => false);
        await Assert.ThrowsAsync<ImportDocumentException>(() => unavailable.CommitAsync(plan));
        int count = fixture.Configuration.Current!.Servers.Length;
        await fixture.Configuration.UpdateSettingsAsync(settings => settings with { DefaultIdentityFilePath = ExplicitKey });
        var service = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => true);
        await Assert.ThrowsAsync<TunnelDeck.Core.Persistence.StateRevisionConflictException>(() => service.CommitAsync(plan));
        Assert.Equal(count, fixture.Configuration.Current.Servers.Length);
    }

    private static ImportPlan Plan(string directives, AppSettings settings, Func<string, bool>? exists = null) =>
        SshConfigParser.Plan($"Host imported\n HostName fixture.example\n User dev\n {directives}\n", new() { Settings = settings }, UserDirectory, exists ?? (_ => true));
}
