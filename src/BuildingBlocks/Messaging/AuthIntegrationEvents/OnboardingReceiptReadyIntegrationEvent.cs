namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 11) — Documents terminó de generar el PDF del recibo de onboarding
/// (<c>DocumentGenerationCompletedIntegrationEvent</c> filtrado por <c>OwnerType=Onboarding</c>) y
/// Auth ya guardó <see cref="ReceiptFileId"/> en el <c>TenantOnboarding</c>. Notification consume
/// este evento para: (1) actualizar la proyección local que <c>OnboardingRegistrationReady</c>
/// consulta best-effort (si llegó a tiempo, el botón de descarga sale en ese mismo email), y (2)
/// enviar un email de seguimiento corto — porque en la práctica el PDF (Playwright + subida a
/// CloudStorage) tarda varios segundos más que el envío del email de bienvenida disparado por el
/// mismo pago, así que (1) casi nunca alcanza a tiempo. <see cref="ReceiptDownloadUrl"/> NO es una
/// URL presignada de MinIO (esas expiran en minutos) — apunta al endpoint mediador propio de Auth
/// (<c>GET /onboarding/receipts/{ReceiptFileId}/download</c>) que resuelve una URL presignada fresca
/// en cada click, así que el link embebido en el email nunca vence.
/// <see cref="IntegrationEvent.TenantId"/> queda en <c>Guid.Empty</c> (el tenant no existe
/// todavía) — <see cref="OnboardingId"/> es la clave de correlación real.
/// </summary>
public sealed record OnboardingReceiptReadyIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required Guid ReceiptFileId { get; init; }
    public required string ReceiptDownloadUrl { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
}
