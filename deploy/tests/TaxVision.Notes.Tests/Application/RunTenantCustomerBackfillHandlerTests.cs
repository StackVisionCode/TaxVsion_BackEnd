using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Domain.Backfill;
using TaxVision.Notes.Domain.Projections;
using Wolverine.Attributes;
using Xunit;

namespace TaxVision.Notes.Tests.Application;

/// <summary>NTS-8: cada página en su transacción, y sin estado hecho si una falla.</summary>
public sealed class RunTenantCustomerBackfillHandlerTests
{
    private static readonly Guid AnaId = Guid.NewGuid();
    private static readonly Guid BeaId = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly InMemoryDirectory _directory = new();
    private readonly InMemoryBackfillStates _states = new();
    private readonly CountingCustomerClient _client = new();
    private readonly NoOpUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_TwoPages_SavesEachInItsOwnTransaction_AndMarksDoneOnTheLast()
    {
        _client.Pages.Add(Page(1, "Ana"));
        _client.Pages.Add(Page(2, "Bea"));

        await RunAsync();

        Assert.Equal(2, _unitOfWork.SaveCount);
        Assert.Equal(2, _directory.Entries.Count);
        Assert.Single(_states.States);
    }

    [Fact]
    public async Task Handle_PageFails_DoesNotMarkDone()
    {
        _client.Pages.Add(Page(1, "Ana"));
        _client.Pages.Add(null);

        await Assert.ThrowsAsync<DependencyNotReadyException>(RunAsync);

        Assert.Single(_directory.Entries);
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task Handle_RetryAfterFailure_DoesNotDuplicate()
    {
        _client.Pages.Add(Page(1, "Ana"));
        _client.Pages.Add(null);
        await Assert.ThrowsAsync<DependencyNotReadyException>(RunAsync);

        _client.Pages[1] = Page(2, "Bea");
        await RunAsync();

        Assert.Equal(2, _directory.Entries.Count);
        Assert.Single(_states.States);
    }

    [Fact]
    public async Task Handle_TenantAlreadySeeded_DoesNotCallCustomer()
    {
        _states.States.Add(TenantBackfillState.Create(_tenant));

        await RunAsync();

        Assert.Equal(0, _client.ListCalls);
    }

    /// <summary>Sin el atributo, la política automática abre una transacción alrededor del HTTP.</summary>
    [Fact]
    public void Handler_IsNonTransactional()
    {
        var handle = typeof(RunTenantCustomerBackfillHandler).GetMethod(
            nameof(RunTenantCustomerBackfillHandler.Handle)
        );

        Assert.NotNull(handle!.GetCustomAttribute<NonTransactionalAttribute>());
    }

    /// <summary>Una lectura vieja no pisa lo que ya aplicó un evento posterior.</summary>
    [Fact]
    public async Task SavePage_OlderReadDoesNotOverwriteANewerEvent()
    {
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        _directory.Entries.Add(
            CustomerDirectoryEntry.Create(_tenant, customerId, "Newer", CustomerDirectoryStatus.Active, now)
        );

        await SaveAsync(
            new SaveCustomerDirectoryPageCommand(
                _tenant,
                [new RemoteCustomerSummary(customerId, "Older", true)],
                now.AddSeconds(-5),
                IsLastPage: true
            )
        );

        Assert.Equal("Newer", Assert.Single(_directory.Entries).DisplayName);
    }

    private Task RunAsync() =>
        RunTenantCustomerBackfillHandler.Handle(
            new RunTenantCustomerBackfillCommand(_tenant),
            _states,
            _client,
            new FakeMessageBus { OnInvoke = message => SaveAsync((SaveCustomerDirectoryPageCommand)message) },
            NullLogger<RunTenantCustomerBackfillCommand>.Instance,
            CancellationToken.None
        );

    private Task SaveAsync(SaveCustomerDirectoryPageCommand command) =>
        SaveCustomerDirectoryPageHandler.Handle(command, _directory, _states, _unitOfWork, CancellationToken.None);

    private static PagedResult<RemoteCustomerSummary> Page(int page, string name) =>
        new([new RemoteCustomerSummary(IdOf(name), name, true)], page, 100, 101);

    private static Guid IdOf(string name) => name == "Ana" ? AnaId : BeaId;
}
