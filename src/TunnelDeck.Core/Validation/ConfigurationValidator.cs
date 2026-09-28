using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Validation;

public static class ConfigurationValidator
{
    public static ValidationResult ValidateServer(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var result = new ValidationBuilder();
        result.Id(server.Id, nameof(server.Id));
        result.Require(HostAddress.IsValidHost(server.Host), nameof(server.Host), UiText.Invariant("Diagnostic_ConfigurationValidator_EnterAnIPAddressOrHostnameWithoutA"));
        result.Port(server.Port, nameof(server.Port));
        result.Require(!string.IsNullOrWhiteSpace(server.Username) && !server.Username.Any(char.IsWhiteSpace) && !server.Username.Any(char.IsControl),
            nameof(server.Username), UiText.Invariant("Diagnostic_ConfigurationValidator_EnterAnSSHUsernameWithoutWhitespaceOrControl"));
        result.Require(server.DisplayName is null || !server.DisplayName.Any(char.IsControl), nameof(server.DisplayName), UiText.Invariant("Diagnostic_ConfigurationValidator_TheDisplayNameCannotContainControlCharacters"));
        result.Require(Enum.IsDefined(server.AuthMethod), nameof(server.AuthMethod), UiText.Invariant("Diagnostic_ConfigurationValidator_ChooseASupportedAuthenticationMethod"));
        if (server.AuthMethod == AuthenticationMethod.PrivateKey)
        {
            result.Require(!string.IsNullOrWhiteSpace(server.IdentityFilePath), nameof(server.IdentityFilePath), UiText.Invariant("Diagnostic_ConfigurationValidator_SelectAPrivateKeyFile"));
        }
        result.Require(server.IdentityFilePath is null || !server.IdentityFilePath.Any(char.IsControl), nameof(server.IdentityFilePath), UiText.Invariant("Diagnostic_ConfigurationValidator_TheKeyPathCannotContainControlCharacters"));
        if (server.CredentialReference is Guid reference) { result.Id(reference, nameof(server.CredentialReference)); }
        result.Utc(server.LastUsedAt, nameof(server.LastUsedAt));
        return result.Build();
    }

    public static ValidationResult ValidateRule(ForwardRule rule, IReadOnlySet<Guid>? availableServers = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var result = new ValidationBuilder();
        result.Id(rule.Id, nameof(rule.Id));
        result.Id(rule.ServerId, nameof(rule.ServerId));
        if (availableServers is not null)
        {
            result.Require(availableServers.Contains(rule.ServerId), nameof(rule.ServerId), UiText.Invariant("Diagnostic_ConfigurationValidator_SelectAnExistingServer"));
        }
        result.Require(Enum.IsDefined(rule.Mode), nameof(rule.Mode), UiText.Invariant("Diagnostic_ConfigurationValidator_ChooseLocalOrRemoteForwarding"));
        result.Port(rule.LocalPort, nameof(rule.LocalPort));
        result.Port(rule.RemotePort, nameof(rule.RemotePort));
        result.Require(HostAddress.IsValidHost(rule.LocalHost), nameof(rule.LocalHost), UiText.Invariant("Diagnostic_ConfigurationValidator_EnterAValidLocalHostWithoutAPort"));
        result.Require(HostAddress.IsValidHost(rule.RemoteHost), nameof(rule.RemoteHost), UiText.Invariant("Diagnostic_ConfigurationValidator_EnterAValidRemoteHostWithoutAPort"));
        if (rule.Mode == ForwardMode.Local)
        {
            result.Require(HostAddress.IsValidBindAddress(rule.LocalHost), nameof(rule.LocalHost), UiText.Invariant("Diagnostic_ConfigurationValidator_TheLocalListeningAddressMustBeAnExplicit"));
        }
        else if (rule.Mode == ForwardMode.Remote)
        {
            result.Require(HostAddress.IsValidBindAddress(rule.RemoteHost), nameof(rule.RemoteHost), UiText.Invariant("Diagnostic_ConfigurationValidator_TheRemoteListeningAddressMustBeAnExplicit"));
            result.Require(!rule.AutoFindAvailablePort, nameof(rule.AutoFindAvailablePort), UiText.Invariant("Diagnostic_ConfigurationValidator_AutomaticPortSearchIsAvailableForLocalForwarding"));
        }
        return result.Build();
    }

    public static ValidationResult ValidateProfile(ForwardProfile profile, IReadOnlySet<Guid>? availableServers = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var result = new ValidationBuilder();
        result.Id(profile.Id, nameof(profile.Id));
        result.Require(!string.IsNullOrWhiteSpace(profile.Name) && !profile.Name.Any(char.IsControl), nameof(profile.Name), UiText.Invariant("Diagnostic_ConfigurationValidator_EnterAProfileNameWithoutControlCharacters"));
        result.Utc(profile.LastUsedAt, nameof(profile.LastUsedAt));
        result.Require(!profile.Rules.IsDefault, nameof(profile.Rules), UiText.Invariant("Diagnostic_ConfigurationValidator_TheRulesCollectionIsRequired"));
        if (!profile.Rules.IsDefault)
        {
            var ids = new HashSet<Guid>();
            for (int index = 0; index < profile.Rules.Length; index++)
            {
                ForwardRule? rule = profile.Rules[index];
                if (rule is null) { result.Require(false, $"Rules[{index}]", UiText.Invariant("Diagnostic_ConfigurationValidator_ARuleCannotBeNull")); continue; }
                result.Require(ids.Add(rule.Id), $"Rules[{index}].Id", UiText.Invariant("Diagnostic_ConfigurationValidator_RuleIDsMustBeUnique"));
                result.Add($"Rules[{index}]", ValidateRule(rule, availableServers));
            }
        }
        return result.Build();
    }

    public static ValidationResult ValidateSettings(AppSettings settings, IReadOnlySet<Guid>? availableServers = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var result = new ValidationBuilder();
        result.Require(UiText.IsSupported(settings.Language), nameof(settings.Language), UiText.Invariant("Diagnostic_ChooseSupportedLanguage"));
        result.Require(settings.SidebarMaximumItems is >= AppSettings.MinimumSidebarItems and <= AppSettings.MaximumSidebarItems,
            nameof(settings.SidebarMaximumItems), UiText.Invariant("Diagnostic_ConfigurationValidator_ChooseFrom3To10ItemsPerSection"));
        result.Require(Enum.IsDefined(settings.DefaultAuthenticationMethod), nameof(settings.DefaultAuthenticationMethod), UiText.Invariant("Diagnostic_ConfigurationValidator_ChooseASupportedDefaultAuthenticationMethod"));
        result.Require(settings.DefaultIdentityFilePath is null ||
            !string.IsNullOrWhiteSpace(settings.DefaultIdentityFilePath) && !settings.DefaultIdentityFilePath.Any(char.IsControl) && Path.IsPathFullyQualified(settings.DefaultIdentityFilePath),
            nameof(settings.DefaultIdentityFilePath), UiText.Invariant("Diagnostic_ConfigurationValidator_ChooseAnAbsolutePathForTheDefaultPrivate"));
        ValidateReference(settings.DefaultServerId, nameof(settings.DefaultServerId));
        ValidateReference(settings.PinnedQuickServerId, nameof(settings.PinnedQuickServerId));
        return result.Build();

        void ValidateReference(Guid? id, string path)
        {
            if (id is not Guid value) { return; }
            result.Id(value, path);
            result.Require(availableServers is null || availableServers.Contains(value), path, UiText.Invariant("Diagnostic_ConfigurationValidator_TheReferencedServerNoLongerExists"));
        }
    }
}
