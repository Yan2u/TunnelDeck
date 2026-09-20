using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Validation;

public static class ConfigurationValidator
{
    public static ValidationResult ValidateServer(RemoteServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        var result = new ValidationBuilder();
        result.Id(server.Id, nameof(server.Id));
        result.Require(HostAddress.IsValidHost(server.Host), nameof(server.Host), "Enter an IP address or hostname without a port, URI scheme or whitespace.");
        result.Port(server.Port, nameof(server.Port));
        result.Require(!string.IsNullOrWhiteSpace(server.Username) && !server.Username.Any(char.IsWhiteSpace) && !server.Username.Any(char.IsControl),
            nameof(server.Username), "Enter an SSH username without whitespace or control characters.");
        result.Require(server.DisplayName is null || !server.DisplayName.Any(char.IsControl), nameof(server.DisplayName), "The display name cannot contain control characters.");
        result.Require(Enum.IsDefined(server.AuthMethod), nameof(server.AuthMethod), "Choose a supported authentication method.");
        if (server.AuthMethod == AuthenticationMethod.PrivateKey)
        {
            result.Require(!string.IsNullOrWhiteSpace(server.IdentityFilePath), nameof(server.IdentityFilePath), "Select a private-key file.");
        }
        result.Require(server.IdentityFilePath is null || !server.IdentityFilePath.Any(char.IsControl), nameof(server.IdentityFilePath), "The key path cannot contain control characters.");
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
            result.Require(availableServers.Contains(rule.ServerId), nameof(rule.ServerId), "Select an existing server.");
        }
        result.Require(Enum.IsDefined(rule.Mode), nameof(rule.Mode), "Choose Local or Remote forwarding.");
        result.Port(rule.LocalPort, nameof(rule.LocalPort));
        result.Port(rule.RemotePort, nameof(rule.RemotePort));
        result.Require(HostAddress.IsValidHost(rule.LocalHost), nameof(rule.LocalHost), "Enter a valid local host without a port or URI scheme.");
        result.Require(HostAddress.IsValidHost(rule.RemoteHost), nameof(rule.RemoteHost), "Enter a valid remote host without a port or URI scheme.");
        if (rule.Mode == ForwardMode.Local)
        {
            result.Require(HostAddress.IsValidBindAddress(rule.LocalHost), nameof(rule.LocalHost), "The local listening address must be an explicit IP address.");
        }
        else if (rule.Mode == ForwardMode.Remote)
        {
            result.Require(HostAddress.IsValidBindAddress(rule.RemoteHost), nameof(rule.RemoteHost), "The remote listening address must be an explicit IP address.");
            result.Require(!rule.AutoFindAvailablePort, nameof(rule.AutoFindAvailablePort), "Automatic port search is available for Local forwarding only.");
        }
        return result.Build();
    }

    public static ValidationResult ValidateProfile(ForwardProfile profile, IReadOnlySet<Guid>? availableServers = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var result = new ValidationBuilder();
        result.Id(profile.Id, nameof(profile.Id));
        result.Require(!string.IsNullOrWhiteSpace(profile.Name) && !profile.Name.Any(char.IsControl), nameof(profile.Name), "Enter a profile name without control characters.");
        result.Utc(profile.LastUsedAt, nameof(profile.LastUsedAt));
        result.Require(!profile.Rules.IsDefault, nameof(profile.Rules), "The rules collection is required.");
        if (!profile.Rules.IsDefault)
        {
            var ids = new HashSet<Guid>();
            for (int index = 0; index < profile.Rules.Length; index++)
            {
                ForwardRule? rule = profile.Rules[index];
                if (rule is null) { result.Require(false, $"Rules[{index}]", "A rule cannot be null."); continue; }
                result.Require(ids.Add(rule.Id), $"Rules[{index}].Id", "Rule IDs must be unique.");
                result.Add($"Rules[{index}]", ValidateRule(rule, availableServers));
            }
        }
        return result.Build();
    }

    public static ValidationResult ValidateSettings(AppSettings settings, IReadOnlySet<Guid>? availableServers = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var result = new ValidationBuilder();
        result.Require(settings.SidebarMaximumItems is >= AppSettings.MinimumSidebarItems and <= AppSettings.MaximumSidebarItems,
            nameof(settings.SidebarMaximumItems), "Choose from 3 to 10 items per section.");
        ValidateReference(settings.DefaultServerId, nameof(settings.DefaultServerId));
        ValidateReference(settings.PinnedQuickServerId, nameof(settings.PinnedQuickServerId));
        return result.Build();

        void ValidateReference(Guid? id, string path)
        {
            if (id is not Guid value) { return; }
            result.Id(value, path);
            result.Require(availableServers is null || availableServers.Contains(value), path, "The referenced server no longer exists.");
        }
    }
}
