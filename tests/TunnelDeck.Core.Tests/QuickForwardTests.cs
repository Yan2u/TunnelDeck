using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class QuickForwardTests
{
    [Theory]
    [InlineData(ForwardMode.Local)]
    [InlineData(ForwardMode.Remote)]
    public void SamePortUsesTheSelectedDirectionAndOnlyLocalCanSearch(ForwardMode mode)
    {
        var model = Model();
        model.PortText = "5432";
        model.ModeIndex = (int)mode;
        var launch = model.Capture()!;
        Assert.True(model.CanStart);
        Assert.Equal(mode, launch.Requested.Mode);
        Assert.Equal(5432, launch.Requested.LocalPort);
        Assert.Equal(5432, launch.Requested.RemotePort);
        Assert.Equal(mode == ForwardMode.Local, launch.Requested.AutoFindAvailablePort);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("3.5")]
    [InlineData("3000.0")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("3,000")]
    [InlineData("invalid")]
    public void InvalidRawTextCannotFallBackToAPreviousValidPort(string raw)
    {
        var model = Model();
        model.PortText = "3000";
        Assert.NotNull(model.Capture());
        model.PortText = raw;
        Assert.Null(model.Capture());
        Assert.NotEmpty(model.PortError);
    }

    [Fact]
    public void AdvancedEndpointsAreIndependentAndUseModeSpecificAddressValidation()
    {
        var model = Model();
        model.PortText = "3000";
        model.UseAdvanced = true;
        Assert.Equal("3000", model.LocalPortText);
        model.LocalPortText = "4000";
        model.RemotePortText = "5000";
        model.RemoteHost = "target.example";
        var local = model.Capture()!.Requested;
        Assert.Equal(4000, local.LocalPort);
        Assert.Equal(5000, local.RemotePort);
        Assert.Equal("target.example", local.RemoteHost);
        model.ModeIndex = 1;
        Assert.Null(model.Capture());
        Assert.NotEmpty(model.RemoteHostError);
        model.RemoteHost = "127.0.0.1";
        model.LocalHost = "target.example";
        Assert.NotNull(model.Capture());
        model.UseAdvanced = false;
        var basic = model.Capture()!.Requested;
        Assert.Equal(3000, basic.LocalPort);
        Assert.Equal(ForwardRule.DefaultHost, basic.LocalHost);
    }

    [Fact]
    public void SelectionAndEditedDefaultsSurviveConfigurationUpdates()
    {
        var model = new QuickForwardModel();
        model.Reconcile(new(), false, false); // Window creation precedes loading persisted settings.
        AppState state = State() with { Settings = new() { DefaultServerId = DomainData.Id(1), PinnedQuickServerId = DomainData.Id(2), AutoFindAvailablePortDefault = false } };
        model.Reconcile(state, false, true);
        Assert.Equal(DomainData.Id(2), model.SelectedServerId);
        Assert.False(model.AutoFindPort);
        model.SelectedServerId = DomainData.Id(1);
        model.PortText = "8080";
        model.Reconcile(state with { Settings = state.Settings with { AutoFindAvailablePortDefault = true } }, false, true);
        Assert.Equal(DomainData.Id(1), model.SelectedServerId);
        Assert.Equal("8080", model.PortText);
        Assert.False(model.AutoFindPort);
        model.Reconcile(state with { Servers = [DomainData.Server(2)] }, false, true);
        Assert.Equal(DomainData.Id(2), model.SelectedServerId);
        model.Reconcile(new(), false, true);
        Assert.Null(model.SelectedServerId);
        Assert.False(model.CanStart);
    }

    [Fact]
    public void PreviewAndReadOnlyStatesCannotEnableTheCommand()
    {
        var model = Model();
        model.PortText = "3000";
        model.Reconcile(State(), true, true);
        Assert.False(model.CanStart);
        model.Reconcile(State(), false, false);
        Assert.False(model.CanStart);
    }

    private static AppState State() => DomainData.State([DomainData.Server(), DomainData.Server(2)], []) with { Settings = new() { DefaultServerId = DomainData.Id(1) } };
    private static QuickForwardModel Model() { var model = new QuickForwardModel(); model.Reconcile(State(), false, true); return model; }
}
