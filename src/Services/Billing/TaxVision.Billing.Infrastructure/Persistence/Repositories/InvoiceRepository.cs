using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.Receipts;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

/// <summary>
/// IgnoreQueryFilters() + tenantId explícito: GetByIdAsync es alcanzable desde consumers/comandos
/// locales de Wolverine (GenerateInvoicePdf, DocumentGenerationCompleted) donde el filtro global
/// fail-closed colapsaría a Guid.Empty. El predicado explícito por TenantId mantiene el aislamiento.
/// </summary>
public sealed class InvoiceRepository(BillingDbContext dbContext) : IInvoiceRepository
{
    private readonly BillingDbContext _dbContext = dbContext;

    // Include(PaymentLinks): ahora es entidad normal (no owned) → EF no la auto-incluye. Sin esto,
    // ActivePaymentLink siempre daría null y AttachPaymentLink duplicaría filas en cada reintento. Las
    // Lines son OwnsMany, así que sí se auto-incluyen.
    public Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default) =>
        _dbContext
            .Invoices.IgnoreQueryFilters()
            .Include(i => i.PaymentLinks)
            .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invoiceId, ct);

    public async Task<IReadOnlyList<Invoice>> ListByTenantAsync(
        Guid tenantId,
        int take,
        CancellationToken ct = default
    ) =>
        await _dbContext
            .Invoices.IgnoreQueryFilters()
            .Include(i => i.PaymentLinks)
            .Where(i => i.TenantId == tenantId && i.DeletedAtUtc == null)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(ct);

    public async Task AddAsync(Invoice invoice, CancellationToken ct = default) =>
        await _dbContext.Invoices.AddAsync(invoice, ct);
}

/// <summary>Numeración server-side. Load-or-create de la secuencia (tenant, período) — el
/// RowVersion resuelve la carrera al persistir el número asignado.</summary>
public sealed class InvoiceNumberSequenceRepository(BillingDbContext dbContext) : IInvoiceNumberSequenceRepository
{
    private readonly BillingDbContext _dbContext = dbContext;

    public async Task<InvoiceNumberSequence> GetOrCreateAsync(
        Guid tenantId,
        string periodKey,
        CancellationToken ct = default
    )
    {
        var sequence = await _dbContext
            .InvoiceNumberSequences.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.PeriodKey == periodKey, ct);

        if (sequence is null)
        {
            sequence = InvoiceNumberSequence.Start(tenantId, periodKey);
            await _dbContext.InvoiceNumberSequences.AddAsync(sequence, ct);
        }

        return sequence;
    }
}

/// <summary>SCAFFOLD B1: stub. La implementación EF real llega en B3 (comprobantes de pago).</summary>
public sealed class PaymentReceiptRepository(BillingDbContext dbContext) : IPaymentReceiptRepository
{
    private readonly BillingDbContext _dbContext = dbContext;

    public Task<PaymentReceipt?> GetByIdAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default) =>
        Task.FromResult<PaymentReceipt?>(null);

    public Task AddAsync(PaymentReceipt receipt, CancellationToken ct = default) => Task.CompletedTask;
}
