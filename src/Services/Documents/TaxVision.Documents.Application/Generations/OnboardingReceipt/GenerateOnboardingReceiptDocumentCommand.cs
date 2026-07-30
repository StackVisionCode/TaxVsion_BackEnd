namespace TaxVision.Documents.Application.Generations.OnboardingReceipt;

/// <summary>
/// PayFlow (Fase 10) — genera el PDF del recibo de pago de un onboarding pago-primero. A diferencia
/// de Invoice, no hay tenant real todavía (el onboarding se paga ANTES de que el tenant exista): la
/// generación se registra bajo <c>PlatformTenant.Id</c> (ver GenerateOnboardingReceiptDocumentHandler),
/// el mismo mecanismo que ya usa Scribe para sus propios assets. El emisor es la plataforma misma
/// (IssuerSnapshot fijo vía IPlatformIssuerProvider), no el tenant — no hay BrandingPayload acá.
/// </summary>
public sealed record GenerateOnboardingReceiptDocumentCommand(
    Guid OnboardingId,
    int DocumentVersion,
    string TemplateKey,
    int TemplateVersion,
    string SourceService,
    string IdempotencyKey,
    string CorrelationId,
    OnboardingReceiptPayload Receipt
);

/// <summary>
/// Datos del recibo tal cual los conoce Auth al momento del pago (PayFlow_Implementation_Plan.md
/// §Fase 10). Los montos llegan en centavos + moneda (mismo shape que
/// OnboardingPaymentSucceededIntegrationEvent en Auth) — Documents no tiene su propio VO Money, y
/// esto evita inventar uno solo para este slice. TransactionReferenceMask son los últimos 4 dígitos
/// de la referencia de pago (nunca el número completo); PaymentMethodMasked es texto libre del
/// proveedor de pago (p.ej. "Visa •••• 4242").
/// </summary>
public sealed record OnboardingReceiptPayload(
    string PayerFirstName,
    string PayerLastName,
    string PayerEmail,
    string PlanName,
    long PricePaidCents,
    string Currency,
    DateTime PaidAtUtc,
    string TransactionReferenceMask,
    string? PaymentMethodMasked
);

/// <summary>Respuesta 202: la generación se registró; el archivo se produce de forma asíncrona.</summary>
public sealed record GenerateOnboardingReceiptDocumentResult(Guid GenerationId, string Status);
