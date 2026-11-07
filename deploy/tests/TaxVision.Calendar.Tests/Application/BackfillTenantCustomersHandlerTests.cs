using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Domain.Backfill;
using TaxVision.Calendar.Domain.Projections;
using Wolverine.Attributes;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>CAL-5: cada página en su transacción, y sin estado hecho si una falla.</summary>
public sealed class BackfillTenantCustomersHandlerTests
{
    private static readonly Guid AnaId = Guid.NewGuid();
    private static readonly Guid BeaId = Guid.NewGuid();

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly InMemoryDirectory _directory = new();
    private readonly InMemoryBackfillStates _states = new();
    private readonly CountingCustomerClient _client = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly List<SaveCustomerDirectoryPageCommand> _pages = [];

    [Fact]
    public async Task TwoPages_SavesEachInItsOwnTransaction_AndMarksDoneOnTheLast()
    {
        _client.Pages.Add(Page(1, totalCount: 101, "Ana"));
        _client.Pages.Add(Page(2, totalCount: 101, "Bea"));

        await RunAsync();

        Assert.Equal(2, _pages.Count);
        Assert.Equal(2, _unitOfWork.Saves);
        Assert.Equal(2, _directory.Entries.Count);
        Assert.Single(_states.States);
    }

    [Fact]
    public async Task PageFails_DoesNotMarkDone_AndKeepsWhatWasSaved()
    {
        _client.Pages.Add(Page(1, totalCount: 101, "Ana"));
        _client.Pages.Add(null);

        await Assert.ThrowsAsync<DependencyNotReadyException>(RunAsync);

        Assert.Single(_directory.Entries);
        Assert.Empty(_states.States);
    }

    [Fact]
    public async Task RetryAfterFailure_DoesNotDuplicate()
    {
        var first = Page(1, totalCount: 101, "Ana");
        _client.Pages.Add(first);
        _client.Pages.Add(null);
        await Assert.ThrowsAsync<DependencyNotReadyException>(RunAsync);

        _client.Pages[1] = Page(2, totalCount: 101, "Bea");
        await RunAsync();

        Assert.Equal(2, _directory.Entries.Count);
        Assert.Single(_states.States);
    }

    [Fact]
    public async Task TenantAlreadySeeded_DoesNotCallCustomer()
    {
        _states.States.Add(TenantBackfillState.Create(_tenant));

        await RunAsync();

        Assert.Equal(0, _client.ListCalls);
    }

    /// <summary>Sin el atributo, la política automática abre una transacción alrededor del HTTP.</summary>
    [Fact]
    public void Handler_IsNonTransactional()
    {
        var handle = typeof(BackfillTenantCustomersHandler).GetMethod(nameof(BackfillTenantCustomersHandler.Handle));

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
        BackfillTenantCustomersHandler.Handle(
            new BackfillTenantCustomersCommand(_tenant),
            _states,
            _client,
            new FakeMessageBus { OnInvoke = message => SaveAsync((SaveCustomerDirectoryPageCommand)message) },
            NullLogger<BackfillTenantCustomersCommand>.Instance,
            CancellationToken.None
        );

    private Task SaveAsync(SaveCustomerDirectoryPageCommand command)
    {
        _pages.Add(command);
        return SaveCustomerDirectoryPageHandler.Handle(
            command,
            _directory,
            _states,
            _unitOfWork,
            CancellationToken.None
        );
    }

    private static PagedResult<RemoteCustomerSummary> Page(int page, int totalCount, string name) =>
        new([new RemoteCustomerSummary(GuidFor(name), name, true)], page, 100, totalCount);

    private static Guid GuidFor(string name) => name == "Ana" ? AnaId : BeaId;
}
