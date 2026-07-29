namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// PayFlow (Fase 11) — Documents terminó de generar el PDF del recibo de onboarding
/// (<c>DocumentGenerationCompletedIntegrationEvent</c> filtrado por <c>OwnerType=Onboarding</c>) y
/// Auth ya guardó <see cref="ReceiptFileId"/> en el <c>TenantOnboarding</c>. Notification (Fase 12)
/// consume este evento para adjuntar el botón "Download receipt" al email de
/// <c>OnboardingRegistrationReady</c>. <see cref="ReceiptDownloadUrl"/> NO es una URL presignada de
/// MinIO (esas expiran en minutos) — apunta al endpoint mediador propio de Auth
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
}
