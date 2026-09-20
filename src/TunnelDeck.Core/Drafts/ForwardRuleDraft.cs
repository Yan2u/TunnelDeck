using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Drafts;

/// <summary>Editable text remains separate from the validated, immutable saved rule.</summary>
public sealed class ForwardRuleDraft
{
    private bool _localAutoSearch = true;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ServerId { get; set; }
    public ForwardMode Mode { get; set; }
    public string LocalHost { get; set; } = ForwardRule.DefaultHost;
    public string LocalPortText { get; set; } = string.Empty;
    public string RemoteHost { get; set; } = ForwardRule.DefaultHost;
    public string RemotePortText { get; set; } = string.Empty;
    public bool AutoFindAvailablePort
    {
        get => Mode == ForwardMode.Local && _localAutoSearch;
        set => _localAutoSearch = value;
    }

    public static ForwardRuleDraft FromRule(ForwardRule rule) => new()
    {
        Id = rule.Id, ServerId = rule.ServerId, Mode = rule.Mode,
        LocalHost = rule.LocalHost, LocalPortText = rule.LocalPort.ToString(CultureInfo.InvariantCulture),
        RemoteHost = rule.RemoteHost, RemotePortText = rule.RemotePort.ToString(CultureInfo.InvariantCulture),
        AutoFindAvailablePort = rule.AutoFindAvailablePort,
    };

    public ForwardRule Build(IReadOnlySet<Guid>? availableServers = null)
    {
        ForwardRule rule = ToCandidate();
        ConfigurationValidator.ValidateRule(rule, availableServers).ThrowIfInvalid();
        return rule;
    }

    internal ForwardRule ToCandidate()
    {
        PortNumber.TryParse(LocalPortText, out int localPort);
        PortNumber.TryParse(RemotePortText, out int remotePort);
        return new ForwardRule
        {
            Id = Id, ServerId = ServerId ?? Guid.Empty, Mode = Mode,
            LocalHost = LocalHost?.Trim() ?? string.Empty, LocalPort = localPort,
            RemoteHost = RemoteHost?.Trim() ?? string.Empty, RemotePort = remotePort,
            AutoFindAvailablePort = AutoFindAvailablePort,
        };
    }
}
