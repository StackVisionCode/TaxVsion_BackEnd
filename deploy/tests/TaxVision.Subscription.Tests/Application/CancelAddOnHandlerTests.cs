using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>BB-14: por InvokeAsync, lo publicado antes de un guardado fallido sale igual tras el reintento en línea.</summary>
public sealed class CancelAddOnHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly CapturingMessageBus _bus = new();
    private readonly FakeSubscriptionAuditLogWriter _audit = new();

    [Fact]
    public async Task Cancel_WhenSaveFails_PublishesNothing()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => CancelAsync(new FailingUnitOfWork()));

        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Cancel_WhenSaved_PublishesInTheSameOrder()
    {
        var result = await CancelAsync(new FakeUnitOfWork());

        Assert.True(result.IsSuccess);
        Assert.Collection(
            _bus.Published,
            message => Assert.IsType<AddOnCancelledIntegrationEvent>(message),
            message => Assert.IsType<RecalculateEntitlementsCommand>(message)
        );
    }

    // Lo que quede sin guardar lo guarda Wolverine después del handler: ese guardado también puede fallar.
    [Fact]
    public async Task Cancel_LeavesNothingPendingOnceItPublishes()
    {
        var auditAtSave = 0;

        await CancelAsync(new ProbeUnitOfWork(() => auditAtSave = _audit.Entries.Count));

        Assert.Equal(1, auditAtSave);
    }

    private Task<Result> CancelAsync(IUnitOfWork unitOfWork)
    {
        var addOn = AddOnTestCatalog.Owned(_tenantId, AddOnTestCatalog.QuotaAddOn("pack.extra"));
        return CancelAddOnHandler.Handle(
            new CancelAddOnCommand(_tenantId, addOn.Id, "not needed", Guid.NewGuid()),
            new FakeTenantAddOnRepo([addOn]),
            unitOfWork,
            _bus,
            new FakeCorrelationContext(),
            _audit,
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );
    }

    private sealed class ProbeUnitOfWork(Action onSave) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            onSave();
            return Task.FromResult(1);
        }
    }
}
