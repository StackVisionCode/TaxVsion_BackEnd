using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Projections.CustomerEvents;
using TaxVision.Calendar.Domain.Backfill;
using TaxVision.Calendar.Domain.Projections;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>CAL-5: el consumer aplica su evento sin HTTP. Traer al resto de los clientes es otro mensaje.</summary>
public sealed class CustomerUpdatedConsumerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly InMemoryDirectory _directory = new();
    private readonly InMemoryBackfillStates _states = new();
    private readonly CountingCustomerClient _client = new();
    private readonly CountingUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task FirstEventOfTenant_EnqueuesBackfill_AppliesEvent_NoInlineHttp()
    {
        var customerId = Guid.NewGuid();

        var command = await HandleAsync(customerId);

        Assert.Equal(0, _client.ListCalls);
        Assert.Equal(_tenant, command?.TenantId);
        Assert.NotNull(await _directory.GetByCustomerIdAsync(_tenant, customerId));
    }

    [Fact]
    public async Task TenantAlreadySeeded_EnqueuesNothing()
    {
        _states.States.Add(TenantBackfillState.Create(_tenant));

        var command = await HandleAsync(Guid.NewGuid());

        Assert.Null(command);
    }

    /// <summary>Sin fila que activar, el consumer sale antes: justo el caso que el backfill arregla.</summary>
    [Fact]
    public async Task ActivatedWithoutEntry_StillEnqueuesBackfill()
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
            new NoCorrelation(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );

        Assert.Equal(_tenant, command?.TenantId);
    }

    private Task<BackfillTenantCustomersCommand?> HandleAsync(Guid customerId) =>
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
            new NoCorrelation(),
            NullLogger<CustomerDirectoryEntry>.Instance,
            CancellationToken.None
        );
}
