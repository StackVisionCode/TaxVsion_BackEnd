using BuildingBlocks.Domain;

namespace TaxVision.Notification.Domain.Onboarding;

/// <summary>
/// PayFlow (Fase 12) — proyección local mínima que resuelve la carrera entre
/// <c>OnboardingRegistrationReadyIntegrationEvent</c> y <c>OnboardingReceiptReadyIntegrationEvent</c>
/// (ambos publicados por Auth de forma independiente y asíncrona; el recibo puede tardar más en
/// generarse que el token de registro). <c>OnboardingReceiptReadyConsumer</c> escribe esta fila
/// apenas llega su evento; <c>OnboardingRegistrationReadyConsumer</c> la consulta por
/// <see cref="OnboardingId"/> — si ya existe, incluye el botón de descarga en el email; si no,
/// el email sale sin él (best-effort, no hay segundo envío).
///
/// Deliberadamente extiende <see cref="BaseEntity"/> (no <c>TenantEntity</c>): el onboarding es
/// pre-tenant (<c>TenantId=Guid.Empty</c> en ambos eventos), así que el <c>HasQueryFilter</c>
/// fail-closed de <c>NotificationDbContext</c> (que solo alcanza a <c>ITenantOwned</c>) no debe
/// aplicar acá — no hay tenant real que filtrar.
/// </summary>
public sealed class OnboardingReceiptLookup : BaseEntity
{
    private OnboardingReceiptLookup() { }

    public Guid OnboardingId { get; private set; }
    public Guid ReceiptFileId { get; private set; }
    public string ReceiptDownloadUrl { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    public static OnboardingReceiptLookup Create(
        Guid onboardingId,
        Guid receiptFileId,
        string receiptDownloadUrl,
        DateTime nowUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            OnboardingId = onboardingId,
            ReceiptFileId = receiptFileId,
            ReceiptDownloadUrl = receiptDownloadUrl,
            CreatedAtUtc = nowUtc,
        };
}
