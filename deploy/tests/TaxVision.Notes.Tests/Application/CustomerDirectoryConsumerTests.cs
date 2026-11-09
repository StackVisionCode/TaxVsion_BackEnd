using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notes.Application.Backfill;
using TaxVision.Notes.Application.Projections.CustomerEvents;
using TaxVision.Notes.Domain.Backfill;
using TaxVision.Notes.Domain.Projections;
using Xunit;

namespace TaxVision.Notes.Tests.Application;

/// <summary>NTS-8: el consumer aplica su evento sin HTTP. Traer al resto de los clientes es otro mensaje.</summary>
public sealed class CustomerDirectoryConsumerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly InMemoryDirectory _directory = new();
    private readonly InMemoryBackfillStates _states = new();
    private readonly CountingCustomerClient _client = new();
    private readonly NoOpUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_FirstEventForTenant_PublishesBackfillCommandWithoutHttp()
    {
        var customerId = Guid.NewGuid();

        var command = await HandleAsync(customerId);

        Assert.Equal(0, _client.ListCalls);
        Assert.Equal(_tenant, command?.TenantId);
        Assert.NotNull(await _directory.GetByCustomerIdAsync(_tenant, customerId));
    }

    [Fact]
    public async Task Handle_TenantAlreadySeeded_PublishesNothing()
    {
        _states.States.Add(TenantBackfillState.Create(_tenant));

        var command = await HandleAsync(Guid.NewGuid());

        Assert.Null(command);
    }

    /// <summary>Sin fila que activar, el consumer sale antes: justo el caso que el backfill arregla.</summary>
    [Fact]
    public async Task Handle_ActivatedWithoutEntry_StillPublishesBackfill()
    {
        var command = await CustomerActivatedConsumer.Handle(
            new CustomerActivatedIntegrationEvent
            {
                TenantId = _tenant,
                CorrelationId = "corr",
                CustomerId = Guid.NewGuid(),
                ActivatedAtUtc = DateTime.UtcNow,
                ActivatedByUserId = Guid.NewGuid(),
            },
            _directory,
            _states,
            _unitOfWork,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Equal(_tenant, command?.TenantId);
    }

    private Task<RunTenantCustomerBackfillCommand?> HandleAsync(Guid customerId) =>
        CustomerUpdatedConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = _tenant,
                CorrelationId = "corr",
                CustomerId = customerId,
                DisplayName = "Jane",
                PrimaryEmail = "jane@example.test",
                Language = "en",
                ModifiedByUserId = Guid.NewGuid(),
            },
            _directory,
            _states,
            _unitOfWork,
            new NoOpCorrelationContext(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );
}
