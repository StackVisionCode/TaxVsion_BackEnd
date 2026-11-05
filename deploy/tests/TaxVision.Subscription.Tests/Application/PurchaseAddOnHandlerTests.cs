using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>SUB-3: la compra directa guarda antes de publicar el cobro.</summary>
public sealed class PurchaseAddOnHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task Handle_SavesBeforePublishingCharge()
    {
        var bus = new CapturingMessageBus();
        var unitOfWork = new PublishedAtSaveUnitOfWork(bus);

        var result = await PurchaseAsync(bus, unitOfWork);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);
        Assert.Single(bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
        Assert.Single(bus.Published.OfType<AddOnActivatedIntegrationEvent>());
    }

    // Simula el reintento en línea de Wolverine: el bus conserva lo publicado en el intento revertido.
    [Fact]
    public async Task Handle_SaveFailsThenInlineRetry_PublishesOneCharge()
    {
        var bus = new CapturingMessageBus();
        var unitOfWork = new PublishedAtSaveUnitOfWork(bus, failFirstSave: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => PurchaseAsync(bus, unitOfWork));
        var retry = await PurchaseAsync(bus, unitOfWork);

        Assert.True(retry.IsSuccess);
        var charge = Assert.Single(bus.Published.OfType<AddOnRenewalDueIntegrationEvent>());
        Assert.Equal(retry.Value, charge.TenantAddOnId);
    }

    private static Task<Result<Guid>> PurchaseAsync(CapturingMessageBus bus, IUnitOfWork unitOfWork)
    {
        // Cada intento relee de cero, como un DbContext nuevo.
        var plan = AddOnTestCatalog.PlanWith("starter", modules: ["documents"]);
        var definition = AddOnTestCatalog.ModuleAddOn("module-addon-email", "email");
        return PurchaseAddOnHandler.Handle(
            new PurchaseAddOnCommand(TenantId, definition.Code.Value, Quantity: 1, AutoRenew: true, Guid.NewGuid()),
            new FakeSubscriptionRepo(AddOnTestCatalog.ActiveSubscription(TenantId, plan)),
            new FakePlanRepo(plan),
            new FakeModuleRepo(AddOnTestCatalog.SellableModule("email")),
            new FakeAddOnDefinitionRepository(definition),
            new NoSettingsRepo(),
            new FakeTenantAddOnRepo(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            new FakeSubscriptionAuditLogWriter(),
            new FakeSubscriptionMetrics(),
            NullLogger<TenantAddOn>.Instance,
            CancellationToken.None
        );
    }

    /// <summary>Registra cuántos mensajes había publicados al guardar; opcionalmente falla el primer guardado.</summary>
    internal sealed class PublishedAtSaveUnitOfWork(CapturingMessageBus bus, bool failFirstSave = false) : IUnitOfWork
    {
        private int _calls;

        public int? PublishedAtFirstSave { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _calls++;
            PublishedAtFirstSave ??= bus.Published.Count;
            if (failFirstSave && _calls == 1)
                throw new InvalidOperationException("transient save failure");
            return Task.FromResult(1);
        }
    }

    private sealed class NoSettingsRepo : ISubscriptionTenantSettingsRepository
    {
        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
