using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.Receipts;
using TaxVision.Billing.Domain.Settings;

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

    /// <summary>BIL-12: factura de onboarding por su Id, con el dueño que tenga hoy. Solo para lo que se pidió bajo
    /// la plataforma y llega después del re-hospedaje; una factura normal no se alcanza por aquí.</summary>
    Task<Invoice?> GetOnboardingInvoiceByIdAsync(Guid invoiceId, CancellationToken ct = default);

    /// <summary>Facturas del tenant, más recientes primero (para la tabla del frontend).
    /// <paramref name="assignedToUserId"/> no null → solo las de clientes asignados a ese usuario.
    /// <paramref name="customerId"/> no null → solo las de ese cliente (se compone con la asignación).</summary>
    Task<IReadOnlyList<Invoice>> ListByTenantAsync(
        Guid tenantId,
        int take,
        CancellationToken ct = default,
        Guid? assignedToUserId = null,
        Guid? customerId = null
    );
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
}

/// <summary>Perfil del emisor (proyección del perfil de la oficina) — uno por tenant.</summary>
public interface IIssuerProfileRepository
{
    Task<IssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(IssuerProfile profile, CancellationToken ct = default);

    /// <summary>Página de perfiles de TODOS los tenants (orden estable por tenant), con el total. Solo
    /// para el listado M2M interno de la importación a Tenant.</summary>
    Task<(IReadOnlyList<IssuerProfile> Items, int TotalCount)> ListPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    );
}

/// <summary>Ajustes de facturación del tenant (plazo, instrucciones de pago, pie) — uno por tenant.</summary>
public interface ITenantBillingSettingsRepository
{
    Task<TenantBillingSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantBillingSettings settings, CancellationToken ct = default);
}

/// <summary>Contador de numeración server-side por (tenant, período).</summary>
public interface IInvoiceNumberSequenceRepository
{
    Task<InvoiceNumberSequence> GetOrCreateAsync(Guid tenantId, string periodKey, CancellationToken ct = default);
}

/// <summary>BIL-5: tenant creado por cada onboarding.</summary>
public interface IOnboardingTenantMapRepository
{
    Task UpsertAsync(Guid onboardingId, Guid tenantId, DateTime nowUtc, CancellationToken ct = default);
    Task<Guid?> FindTenantIdAsync(Guid onboardingId, CancellationToken ct = default);
}

/// <summary>Acceso a comprobantes de pago. SCAFFOLD B1: se implementa en B3.</summary>
public interface IPaymentReceiptRepository
{
    Task<PaymentReceipt?> GetByIdAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default);
    Task AddAsync(PaymentReceipt receipt, CancellationToken ct = default);
}
