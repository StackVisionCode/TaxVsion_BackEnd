using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.Receipts;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Acceso a facturas del tenant.</summary>
public interface IInvoiceRepository
{
    /// <param name="assignedToUserId">Si no es null, solo devuelve la factura si su cliente está asignado a
    /// ese usuario (visibilidad P2); si no lo está → null (404). null = sin restricción (admin/view_all o flag off).</param>
    Task<Invoice?> GetByIdAsync(
        Guid tenantId,
        Guid invoiceId,
        CancellationToken ct = default,
        Guid? assignedToUserId = null
    );

    /// <summary>Factura de onboarding por su OnboardingId (independiente del tenant dueño). Se usa para la
    /// idempotencia del alta pre-tenant y para el backfill del tenant real. IgnoreQueryFilters interno.</summary>
    Task<Invoice?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);

    /// <summary>Facturas del tenant, más recientes primero (para la tabla del frontend).
    /// <paramref name="assignedToUserId"/> no null → solo las de clientes asignados a ese usuario.</summary>
    Task<IReadOnlyList<Invoice>> ListByTenantAsync(
        Guid tenantId,
        int take,
        CancellationToken ct = default,
        Guid? assignedToUserId = null
    );
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
