using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.Core.Tests;

public sealed class ImportTransactionTests
{
    [Fact]
    public async Task ImportIsOneCommitAndCannotBeSubmittedTwice()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        AppState before = fixture.Configuration.Current!;
        var service = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => true);
        ImportPlan plan = ImportPlanner.Plan(TransferPlannerTests.Export(), before, _ => true);
        int publications = 0;
        fixture.Configuration.StateChanged += _ => publications++;
        await service.CommitAsync(plan);
        Assert.Equal(1, publications); Assert.Equal(before.Revision + 1, fixture.Configuration.Current!.Revision);
        Assert.Equal(before.Servers.Length + 2, fixture.Configuration.Current.Servers.Length);
        Assert.Equal(before.Servers[0], fixture.Configuration.Current.Servers[0]);
        Assert.Equal(before.Settings, fixture.Configuration.Current.Settings);
        Assert.Empty(fixture.Factory.Requests);
        await Assert.ThrowsAsync<StateRevisionConflictException>(() => service.CommitAsync(plan));
        Assert.Equal(1, publications);
    }

    [Fact]
    public async Task CancelledPreviewAndCancelledCommitDoNotChangeState()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        AppState before = fixture.Configuration.Current!;
        ImportPlan plan = ImportPlanner.Plan(TransferPlannerTests.Export(), before, _ => true);
        Assert.Same(before, fixture.Configuration.Current);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var service = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CommitAsync(plan, cancelled.Token));
        Assert.Same(before, fixture.Configuration.Current); Assert.Same(before, fixture.Store.State);
    }

    [Fact]
    public async Task DeletionAfterPreviewPreventsResurrectionAndFreshPreviewUsesNewIds()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        var service = new ResourceImportService(fixture.Configuration, fixture.Backend.Connections.Manager.Work, _ => true);
        ImportPlan stale = ImportPlanner.Plan(TransferPlannerTests.Export(), fixture.Configuration.Current!, _ => true);
        Assert.True((await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]))).Succeeded);
        await Assert.ThrowsAsync<StateRevisionConflictException>(() => service.CommitAsync(stale));
        Assert.DoesNotContain(fixture.Configuration.Current!.Servers, server => server.Id == DomainData.Id(1));
        ImportPlan fresh = ImportPlanner.Plan(TransferPlannerTests.Export(), fixture.Configuration.Current!, _ => true);
        await service.CommitAsync(fresh);
        Assert.DoesNotContain(fixture.Configuration.Current!.Servers, server => server.Id == DomainData.Id(1));
        Assert.All(fresh.Servers, server => Assert.Contains(fixture.Configuration.Current.Servers, saved => saved.Id == server.Id));
    }

    [Fact]
    public async Task ImportBetweenBulkConfirmationAndDeletionRequiresConfirmationAgain()
    {
        await using var fixture = await DeletionFixture.CreateAsync();
        DeletionImpact confirmed = fixture.Deletion.CalcAllDeletionImpact(ResourceKind.Server);
        var service = new ResourceImportService(fixture.Configuration, fixture.Backend.Connections.Manager.Work, _ => true);
        await service.CommitAsync(ImportPlanner.Plan(TransferPlannerTests.Export(), fixture.Configuration.Current!, _ => true));
        DeletionResult result = await fixture.Deletion.DeleteConfirmedAsync(confirmed);
        Assert.Equal(DeletionOutcome.NeedsConfirmation, result.Outcome);
        Assert.Equal(4, result.Impact.ResourceCount);
        Assert.Equal(4, fixture.Configuration.Current!.Servers.Length);
    }

    [Fact]
    public async Task ShutdownAdmissionRejectsAnOtherwiseValidPreview()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        AppState before = fixture.Configuration.Current!;
        ImportPlan plan = ImportPlanner.Plan(TransferPlannerTests.Export(), before, _ => true);
        await fixture.Manager.Work.BeginShutdown();
        var service = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => true);
        ConnectionException failure = await Assert.ThrowsAsync<ConnectionException>(() => service.CommitAsync(plan));
        Assert.Equal(ConnectionFailure.ShuttingDown, failure.Failure);
        Assert.Same(before, fixture.Configuration.Current);
    }

    [Fact]
    public async Task KeyDisappearingAfterPreviewPreventsEveryResourceFromBeingCommitted()
    {
        await using var fixture = await ConnectionFixture.CreateAsync();
        AppState before = fixture.Configuration.Current!;
        TunnelDeckExport export = TransferPlannerTests.Export();
        export = export with { Servers = export.Servers.SetItem(1, export.Servers[1] with
        { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = @"C:\fixture\key" }) };
        ImportPlan plan = ImportPlanner.Plan(export, before, _ => true);
        var service = new ResourceImportService(fixture.Configuration, fixture.Manager.Work, _ => false);
        await Assert.ThrowsAsync<ImportDocumentException>(() => service.CommitAsync(plan));
        Assert.Same(before, fixture.Configuration.Current);
    }
}
