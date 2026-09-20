using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class ConfigurationValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("3000.5")]
    [InlineData("3000.0")]
    [InlineData("65536")]
    [InlineData("1e3")]
    [InlineData("+22")]
    [InlineData("0x16")]
    [InlineData("NaN")]
    [InlineData("1,000")]
    public void InvalidRawPortInputCannotFallBackToTheLastValue(string? text)
    {
        Assert.False(PortNumber.TryParse(text, out int parsed));
        Assert.Equal(0, parsed);
        ForwardRuleDraft draft = ForwardRuleDraft.FromRule(DomainData.Rule());
        draft.LocalPortText = text!;
        DomainValidationException error = Assert.Throws<DomainValidationException>(() => draft.Build());
        Assert.Contains(error.Result.Issues, issue => issue.Path == "LocalPort");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 22 ", 22)]
    [InlineData("65535", 65535)]
    public void WholePortsAtTheBoundsAreAccepted(string text, int expected)
    {
        Assert.True(PortNumber.TryParse(text, out int parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(22.5)]
    [InlineData(65536)]
    public void NumberBoxValuesMustBeFiniteWholePorts(double value) => Assert.False(PortNumber.TryFromNumber(value, out _));

    [Fact]
    public void BindAndTargetValidationFollowTheSelectedMode()
    {
        Assert.True(ConfigurationValidator.ValidateRule(DomainData.Rule() with { RemoteHost = "db.internal" }).IsValid);
        Assert.False(ConfigurationValidator.ValidateRule(DomainData.Rule() with { LocalHost = "localhost" }).IsValid);
        Assert.True(ConfigurationValidator.ValidateRule(DomainData.Rule(mode: ForwardMode.Remote) with { LocalHost = "localhost", RemoteHost = "::1" }).IsValid);
        Assert.False(ConfigurationValidator.ValidateRule(DomainData.Rule(mode: ForwardMode.Remote) with { RemoteHost = "remote-host.example" }).IsValid);
        Assert.False(ConfigurationValidator.ValidateRule(DomainData.Rule(mode: ForwardMode.Remote) with { AutoFindAvailablePort = true }).IsValid);
    }

    [Theory]
    [InlineData("https://host")]
    [InlineData("host:22")]
    [InlineData("bad host")]
    [InlineData("host\nname")]
    [InlineData("-oProxyCommand=anything")]
    public void HostInputDoesNotAcceptUrisPortsOrCommandArguments(string host) =>
        Assert.False(ConfigurationValidator.ValidateServer(DomainData.Server() with { Host = host }).IsValid);

    [Fact]
    public void OptionalAndRepeatedDisplayNamesDoNotChangeResourceIdentity()
    {
        RemoteServer first = DomainData.Server(1, "same");
        RemoteServer second = DomainData.Server(2, "same");
        Assert.True(AppStateValidator.Validate(DomainData.State([first, second], [])).IsValid);
        Assert.True(ConfigurationValidator.ValidateServer(first with { DisplayName = null }).IsValid);
        Assert.True(ConfigurationValidator.ValidateServer(first with { DisplayName = string.Empty }).IsValid);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void PrivateKeysNeedAPathAndUnknownAuthenticationIsRejected()
    {
        Assert.False(ConfigurationValidator.ValidateServer(DomainData.Server() with { AuthMethod = AuthenticationMethod.PrivateKey }).IsValid);
        Assert.True(ConfigurationValidator.ValidateServer(DomainData.Server() with { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = "C:\\keys\\example" }).IsValid);
        Assert.False(ConfigurationValidator.ValidateServer(DomainData.Server() with { AuthMethod = (AuthenticationMethod)9 }).IsValid);
        Assert.False(ConfigurationValidator.ValidateServer(DomainData.Server() with { CredentialReference = Guid.Empty }).IsValid);
    }

    [Fact]
    public void InvalidProfileFieldsAreReportedTogetherWithRulePaths()
    {
        ForwardProfile profile = DomainData.Profile(1, DomainData.Rule(localPort: 0), DomainData.Rule(2, server: 9, remotePort: 65536)) with { Name = " " };
        ValidationResult result = ConfigurationValidator.ValidateProfile(profile, new HashSet<Guid> { DomainData.Id(1) });
        Assert.Contains(result.Issues, issue => issue.Path == "Name");
        Assert.Contains(result.Issues, issue => issue.Path == "Rules[0].LocalPort");
        Assert.Contains(result.Issues, issue => issue.Path == "Rules[1].ServerId");
        Assert.Contains(result.Issues, issue => issue.Path == "Rules[1].RemotePort");
    }
}
