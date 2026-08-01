namespace TaxVision.Auth.Application.Onboarding.Abstractions;

/// <summary>PayFlow (Fase 17) — métricas OTel del ciclo de vida de un onboarding pago-primero.
/// Contadores por tipo de evento de negocio, no por HTTP request — la duración se mide desde
/// <c>TenantOnboarding.CreatedAtUtc</c> hasta el momento en que se alcanza un estado terminal
/// (Completed/Cancelled/Refunded/Expired).</summary>
public interface IOnboardingMetrics
{
    void RecordStarted();
    void RecordCompleted();
    void RecordFailed(string step);
    void RecordManualReview();
    void RecordDurationSeconds(double seconds, string outcome);
    void RecordHttpClientRetry(string clientName);
    void RecordHttpClientCircuitOpened(string clientName);
}
