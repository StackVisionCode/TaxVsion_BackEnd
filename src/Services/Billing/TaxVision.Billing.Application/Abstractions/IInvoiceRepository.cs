using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.Receipts;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Acceso a facturas del tenant.</summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default);

    /// <summary>Factura de onboarding por su OnboardingId (independiente del tenant dueño). Se usa para la
    /// idempotencia del alta pre-tenant y para el backfill del tenant real. IgnoreQueryFilters interno.</summary>
    Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);

    /// <summary>Facturas del tenant, más recientes primero (para la tabla del frontend).</summary>
    Task<IReadOnlyList<Invoice>> ListByTenantAsync(Guid tenantId, int take, CancellationToken ct = default);
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
}

/// <summary>Perfil del emisor (datos de la empresa) del tenant — uno por tenant.</summary>
public interface IIssuerProfileRepository
{
    Task<IssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(IssuerProfile profile, CancellationToken ct = default);
}

/// <summary>Contador de numeración server-side por (tenant, período).</summary>
public interface IInvoiceNumberSequenceRepository
{
    Task<InvoiceNumberSequence> GetOrCreateAsync(Guid tenantId, string periodKey, CancellationToken ct = default);
}

/// <summary>Acceso a comprobantes de pago. SCAFFOLD B1: se implementa en B3.</summary>
public interface IPaymentReceiptRepository
{
    Task<PaymentReceipt?> GetByIdAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default);
    Task AddAsync(PaymentReceipt receipt, CancellationToken ct = default);
}
