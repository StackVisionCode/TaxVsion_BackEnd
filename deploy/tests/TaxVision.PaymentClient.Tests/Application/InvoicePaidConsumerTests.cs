using BuildingBlocks.Common;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Payables.IntegrationEvents;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Application;

public sealed class InvoicePaidConsumerTests
{
    [Fact]
    public async Task Paid_invoice_settles_the_payable_and_revokes_the_active_link()
    {
        var tenantId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var payable = PayableReference
            .Create(
                tenantId,
                PaymentPurposeKind.InvoicePayment,
                invoiceId.ToString(),
                Money.Create(12500, "USD").Value,
                now
            )
            .Value;
        var link = PaymentLink
            .Create(
                tenantId,
                null,
                Money.Create(12500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, invoiceId.ToString()).Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(1),
                Guid.NewGuid(),
                now
            )
            .Value;
        var unitOfWork = new CountingUnitOfWork();

        await InvoicePaidConsumer.Handle(
            Event(tenantId, invoiceId),
            new PayableRepository(payable),
            new LinkRepository(link),
            unitOfWork,
            new NoOpCorrelationContext(),
            NullLoggerFactory.Instance,
            CancellationToken.None
        );

        Assert.True(payable.IsSettled);
        Assert.Equal(PaymentLinkStatus.Revoked, link.Status);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Missing_payable_is_a_noop()
    {
        var unitOfWork = new CountingUnitOfWork();

        await InvoicePaidConsumer.Handle(
            Event(Guid.NewGuid(), Guid.NewGuid()),
            new PayableRepository(null),
            new LinkRepository(null),
            unitOfWork,
            new NoOpCorrelationContext(),
            NullLoggerFactory.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static InvoicePaidIntegrationEvent Event(Guid tenantId, Guid invoiceId) =>
        new()
        {
            TenantId = tenantId,
            InvoiceId = invoiceId,
            InvoiceNumber = "INV-2026-001",
            CustomerId = Guid.NewGuid(),
            Currency = "USD",
            AmountPaidCents = 12500,
            PaidAtUtc = DateTime.UtcNow,
            PaymentMethod = "Card",
        };

    private sealed class PayableRepository(PayableReference? payable) : IPayableReferenceRepository
    {
        public Task<PayableReference?> GetByReferenceAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(payable);

        public Task<PayableReference?> GetByExternalReferenceAsync(
            Guid tenantId,
            PaymentPurposeKind kind,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(payable);

        public Task AddAsync(PayableReference item, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class LinkRepository(PaymentLink? link) : IPaymentLinkRepository
    {
        public Task<PaymentLink?> GetByIdAsync(Guid paymentLinkId, Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(link);

        public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default) =>
            Task.FromResult(link);

        public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(
            Guid tenantPaymentId,
            CancellationToken ct = default
        ) => Task.FromResult(link);

        public Task<PaymentLink?> GetActiveByExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(link);

        public Task<bool> AnyUsedForExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
            Guid tenantId,
            PaymentLinkStatus? status,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PaymentLink>>([]);

        public Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PaymentLink>>([]);

        public Task AddAsync(PaymentLink item, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CountingUnitOfWork : IUnitOfWork
    {
        public int SaveCalls { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCalls++;
            return Task.FromResult(1);
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
