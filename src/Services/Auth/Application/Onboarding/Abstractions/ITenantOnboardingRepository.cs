using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface ITenantOnboardingRepository
{
    Task<TenantOnboarding?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 13) — resuelve el token de registro público (preview/complete/status)
    /// por su hash SHA-256 normalizado en minúsculas, contra el índice único filtrado de
    /// RegistrationTokenHash (TenantOnboardingConfiguration).</summary>
    Task<TenantOnboarding?> GetByRegistrationTokenHashAsync(
        string registrationTokenHash,
        CancellationToken ct = default
    );

    /// <summary>Resuelve el onboarding dueño de un recibo por su FileId. Respalda el endpoint público
    /// de descarga: solo un FileId que sea realmente un recibo emitido puede resolverse, así el
    /// endpoint anónimo deja de ser un resolver genérico de archivos por GUID.</summary>
    Task<TenantOnboarding?> GetByReceiptFileIdAsync(Guid receiptFileId, CancellationToken ct = default);

    Task AddAsync(TenantOnboarding onboarding, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 17) — onboardings ProvisioningFailed con reintento automático
    /// programado y vencido. <c>OnboardingRetryScheduler</c> los recorre en cada tick.</summary>
    Task<IReadOnlyList<TenantOnboarding>> GetDueForRetryAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Onboardings pagados que siguen en RegistrationPending, sin email de registro enviado,
    /// cuyo pago se liquidó antes del corte (ventana del recordatorio ya vencida). Los recorre el
    /// OnboardingRegistrationReminderScheduler para publicar el email diferido.</summary>
    Task<IReadOnlyList<TenantOnboarding>> GetRegistrationRemindersDueAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>PayFlow (Fase 17) — listado paginado para <c>OnboardingAdminController</c>, filtrable
    /// por Status (típicamente ManualReview/ProvisioningFailed). Cross-tenant a propósito: es un
    /// endpoint PlatformAdmin-only, igual que el resto de los admin controllers del monorepo.</summary>
    Task<(IReadOnlyList<TenantOnboarding> Items, int TotalCount)> GetPagedAdminAsync(
        TenantOnboardingStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}
