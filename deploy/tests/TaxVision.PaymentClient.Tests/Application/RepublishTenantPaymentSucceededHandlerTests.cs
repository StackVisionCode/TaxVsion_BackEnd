using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentClient.Application.Admin.Commands.RepublishTenantPaymentSucceeded;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Reposición de la señal "pagado" que el dueño aprueba tras la Ola 0: por defecto solo
/// simula, y al ejecutarla deja auditado quién la corrió y qué pagos republicó.</summary>
public sealed class RepublishTenantPaymentSucceededHandlerTests
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly List<object> _published = [];
    private readonly Guid _actor = Guid.NewGuid();

    public RepublishTenantPaymentSucceededHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_databaseName);

    [Fact]
    public async Task Republish_DryRunByDefault_ListsEligibleAndPublishesNothing()
    {
        var (succeeded, refunded, failed) = await SeedAsync();
        var unknown = Guid.NewGuid();

        var result = await RepublishAsync(
            new RepublishTenantPaymentSucceededCommand([succeeded.Id, refunded.Id, failed.Id, unknown], _actor)
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.DryRun);
        Assert.Equal(new[] { succeeded.Id, refunded.Id }.Order(), result.Value.Republished.Order());
        Assert.Equal(new[] { failed.Id, unknown }.Order(), result.Value.Skipped.Select(s => s.TenantPaymentId).Order());
        Assert.Empty(_published);
        Assert.Empty(await AuditAsync());
    }

    [Fact]
    public async Task Republish_Execute_PublishesEligibleAndAuditsActor()
    {
        var (succeeded, refunded, failed) = await SeedAsync();

        var result = await RepublishAsync(
            new RepublishTenantPaymentSucceededCommand([succeeded.Id, refunded.Id, failed.Id], _actor, DryRun: false)
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.DryRun);
        var events = _published.OfType<TenantPaymentSucceededIntegrationEvent>().ToList();
        Assert.Equal(new[] { succeeded.Id, refunded.Id }.Order(), events.Select(e => e.TenantPaymentId).Order());
        Assert.Equal(succeeded.TenantId, events.Single(e => e.TenantPaymentId == succeeded.Id).TenantId);

        var audit = await AuditAsync();
        Assert.Equal(new[] { succeeded.Id, refunded.Id }.Order(), audit.Select(a => a.AggregateId).Order());
        Assert.All(audit, entry => Assert.Equal(_actor, entry.ActorUserId));
    }

    [Fact]
    public async Task Republish_MoreThan500Ids_IsRejected()
    {
        var ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToList();

        var result = await RepublishAsync(new RepublishTenantPaymentSucceededCommand(ids, _actor, DryRun: false));

        Assert.True(result.IsFailure);
        Assert.Equal("Republish.TooManyIds", result.Error.Code);
        Assert.Empty(_published);
    }

    private Task<Result<RepublishTenantPaymentSucceededResponse>> RepublishAsync(
        RepublishTenantPaymentSucceededCommand command
    ) =>
        RepublishTenantPaymentSucceededHandler.Handle(
            command,
            new TenantPaymentRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            InMemoryPaymentClient.RecordingBus(_published),
            new FixedCorrelationContext(),
            CancellationToken.None
        );

    private async Task<List<PaymentAuditEntry>> AuditAsync()
    {
        await using var fresh = InMemoryPaymentClient.CreateDb(_databaseName);
        return await fresh
            .AuditEntries.IgnoreQueryFilters()
            .Where(a => a.Action == PaymentAuditAction.TenantPaymentSignalRepublished)
            .ToListAsync();
    }

    // Pagos de dos oficinas distintas: la reposición es de plataforma (cross-tenant).
    private async Task<(TenantPayment Succeeded, TenantPayment Refunded, TenantPayment Failed)> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var succeeded = NewProcessing(Guid.NewGuid(), now);
        succeeded.MarkSucceeded(now, Guid.Empty);

        var refunded = NewProcessing(Guid.NewGuid(), now);
        refunded.MarkSucceeded(now, Guid.Empty);
        refunded.RefundPartial(Money.Create(500, "USD").Value, "Partial refund.", Guid.Empty, now);

        var failed = NewProcessing(succeeded.TenantId, now);
        failed.MarkFailed("card_declined", "Declined.", false, null, Guid.Empty, now);

        _db.TenantPayments.AddRange(succeeded, refunded, failed);
        await _db.SaveChangesAsync();
        return (succeeded, refunded, failed);
    }

    private static TenantPayment NewProcessing(Guid tenantId, DateTime now)
    {
        var payment = TenantPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"republish-{Guid.NewGuid():N}").Value,
                Money.Create(2500, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, $"inv-{Guid.NewGuid():N}").Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                now
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, $"pi_{Guid.NewGuid():N}").Value,
            null,
            null,
            Guid.Empty,
            now
        );
        return payment;
    }
}
