namespace TaxVision.Auth.Application.Onboarding.Abstractions;

// 2026-10-16 caught live: dos POST /onboarding/reconcile-payment concurrentes para el mismo
// onboardingId pasaron el guard en memoria del aggregate y publicaron DOS eventos de registro ready,
// generaron DOS invoices y DOS receipts. Serializer in-memory por OnboardingId cierra la ventana
// cuando ambos requests caen al mismo pod (single-instance local y la mayoría de prod con
// sticky sessions). Para multi-instance real, extender la implementación con sp_getapplock.
public interface IOnboardingReconcileSerializer
{
    Task<IAsyncDisposable> AcquireAsync(Guid onboardingId, CancellationToken ct);
}
