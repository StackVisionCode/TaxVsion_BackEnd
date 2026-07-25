using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Receipts;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>SCAFFOLD B1: stub. La implementación EF real (DbSets, query filters por tenant,
/// includes) llega en B2 junto con el modelo de persistencia.</summary>
public sealed class InvoiceRepository(BillingDbContext dbContext) : IInvoiceRepository
{
    private readonly BillingDbContext _dbContext = dbContext;

    public Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default) =>
        Task.FromResult<Invoice?>(null);

    public Task AddAsync(Invoice invoice, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>SCAFFOLD B1: stub. La implementación EF real llega en B3.</summary>
public sealed class PaymentReceiptRepository(BillingDbContext dbContext) : IPaymentReceiptRepository
{
    private readonly BillingDbContext _dbContext = dbContext;

    public Task<PaymentReceipt?> GetByIdAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default) =>
        Task.FromResult<PaymentReceipt?>(null);

    public Task AddAsync(PaymentReceipt receipt, CancellationToken ct = default) => Task.CompletedTask;
}
