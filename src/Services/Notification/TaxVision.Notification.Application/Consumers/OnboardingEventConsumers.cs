using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Onboarding;

namespace TaxVision.Notification.Application.Consumers;

// ---------------------------------------------------------------------------
// PayFlow (Fase 12) — 2 templates nuevos en Scribe (onboarding.otp_requested.v1,
// onboarding.registration_ready.v1) + estos 3 consumers. El tercero
// (OnboardingReceiptReadyConsumer) no estaba en la lista original del plan ("2 consumers"), pero es
// imprescindible para que "si OnboardingReceiptReady ya llegó, incluir receiptDownloadUrl" (texto
// literal del plan) sea algo real: sin él, OnboardingRegistrationReadyConsumer no tendría ninguna
// forma de saber si el recibo ya está listo. Es 100% "Notification consumers" — dentro de lo
// permitido — y replica el patrón ya usado varias veces en este servicio (proyección local
// alimentada por un consumer, leída por otro: NotificationRecipientPermissionsProjection,
// NotificationRecipientRolePermissionsProjection).
// ---------------------------------------------------------------------------

public static class OnboardingOtpRequestedConsumer
{
    public static async Task Handle(
        OnboardingOtpRequestedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // Mismo fallback que UserRegisteredConsumer.ResolveName: sin nombre, se usa la parte
            // local del email — evita mezclar un fallback en inglés ("there", literal del texto del
            // plan) en un template que por lo demás está en español.
            var firstName = string.IsNullOrWhiteSpace(evt.FirstNameHint) ? evt.Email.Split('@')[0] : evt.FirstNameHint!;
            var expiresInMinutes = Math.Max(1, (int)Math.Ceiling((evt.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes));

            var render = (
                await scribeClient.RenderAsync(
                    "onboarding.otp_requested.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["otp_code"] = evt.OtpCode,
                        ["first_name"] = firstName,
                        ["expires_in_minutes"] = expiresInMinutes,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("onboarding.otp_requested.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "onboarding.otp_code",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

public static class OnboardingRegistrationReadyConsumer
{
    public static async Task Handle(
        OnboardingRegistrationReadyIntegrationEvent evt,
        IOnboardingTokenClient tokenClient,
        IOnboardingReceiptLookupRepository receiptLookups,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        ILogger<OnboardingRegistrationReadyIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // A diferencia del resto de este archivo, esta llamada no es best-effort: sin la URL de
            // registro real (resuelta contra el TokenReference del lado de Auth) el email no tiene
            // ningún propósito. Auditoría F15 — hasta acá este `if` hacía `return;` sin relanzar, así
            // que Wolverine NUNCA reintentaba (el comentario original ya decía "Wolverine reintenta
            // el evento completo", pero el código no lo hacía). Ahora sí lanza, y como Auth resuelve
            // esta referencia con PeekAsync (no GETDEL) el reintento encuentra el mismo raw token
            // dentro de la ventana de 30s en vez de fallar por "ya consumido".
            var urlResult = await tokenClient.ResolveRegistrationUrlAsync(evt.TokenReference, ct);
            if (urlResult.IsFailure)
            {
                logger.LogError(
                    "Could not resolve the registration URL for onboarding {OnboardingId}: {ErrorCode}. Wolverine will retry.",
                    evt.OnboardingId,
                    urlResult.Error.Code
                );
                throw new InvalidOperationException(
                    $"Could not resolve the registration URL for onboarding {evt.OnboardingId}: {urlResult.Error.Code}."
                );
            }

            // Best-effort: si OnboardingReceiptReady todavía no llegó (la generación del PDF es
            // asíncrona y puede tardar más que esto), el email sale sin el botón de descarga — no
            // hay un segundo envío cuando el recibo aparece después.
            var receiptLookup = await receiptLookups.GetByOnboardingIdAsync(evt.OnboardingId, ct);

            var render = (
                await scribeClient.RenderAsync(
                    "onboarding.registration_ready.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["first_name"] = evt.FirstName,
                        // PlanName es nullable hasta Fase 16 (Auth no tiene acceso al catálogo de
                        // Subscription todavía) — mismo gap ya documentado en el evento.
                        ["plan_name"] = evt.PlanName ?? "tu plan",
                        ["price_formatted"] = evt.PriceFormatted,
                        ["paid_at"] = evt.PaidAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["registration_url"] = urlResult.Value,
                        ["receipt_download_url"] = receiptLookup?.ReceiptDownloadUrl,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("onboarding.registration_ready.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "onboarding.registration_ready",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}

/// <summary>
/// Actualiza la proyección local que <c>OnboardingRegistrationReadyConsumer</c> consulta
/// best-effort, y ADEMÁS manda un email de seguimiento corto con el link de descarga — en la
/// práctica el PDF (Playwright + subida) siempre tarda más que el email de bienvenida disparado por
/// el mismo pago, así que ese primer intento best-effort estructuralmente nunca alcanza a tiempo (ver
/// comentario de cabecera de este archivo); sin este segundo envío, el recibo nunca llegaba por
/// ningún canal.
/// </summary>
public static class OnboardingReceiptReadyConsumer
{
    public static async Task Handle(
        OnboardingReceiptReadyIntegrationEvent evt,
        IOnboardingReceiptLookupRepository receiptLookups,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            // Idempotente ante redelivery — el índice único de OnboardingId también lo garantiza,
            // pero chequear antes evita una ConflictException esperable en el camino feliz, y evita
            // reenviar el email de seguimiento en cada redelivery.
            var existing = await receiptLookups.GetByOnboardingIdAsync(evt.OnboardingId, ct);
            if (existing is not null)
                return;

            await receiptLookups.AddAsync(
                OnboardingReceiptLookup.Create(
                    evt.OnboardingId,
                    evt.ReceiptFileId,
                    evt.ReceiptDownloadUrl,
                    DateTime.UtcNow
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);

            var render = (
                await scribeClient.RenderAsync(
                    "onboarding.receipt_ready.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["first_name"] = evt.FirstName,
                        ["receipt_download_url"] = evt.ReceiptDownloadUrl,
                        ["product_name"] = portal.Value.ProductName,
                    },
                    ct
                )
            ).EnsureRendered("onboarding.receipt_ready.v1");

            await gateway.QueueEmailAsync(
                new EmailDispatchRequest(
                    TenantId: evt.TenantId,
                    To: evt.Email,
                    Subject: render.Subject,
                    HtmlBody: render.Html,
                    TextBody: render.Text ?? string.Empty,
                    TemplateKey: "onboarding.receipt_ready",
                    RelatedEventId: evt.EventId,
                    CorrelationId: correlation.CorrelationId,
                    InlineAssets: render.InlineAssets
                ),
                ct
            );
        }
    }
}
