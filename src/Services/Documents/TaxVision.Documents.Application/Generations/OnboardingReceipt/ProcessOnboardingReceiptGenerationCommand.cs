namespace TaxVision.Documents.Application.Generations.OnboardingReceipt;

/// <summary>
/// Comando local (cola durable de Wolverine) que ejecuta la generación real del recibo de onboarding
/// de forma asíncrona. Lo publica <see cref="GenerateOnboardingReceiptDocumentHandler"/> dentro de la
/// misma transacción que persiste la generación Requested (outbox durable). Transporta los DATOS del
/// recibo (no bytes); si el mensaje se reintenta, Wolverine lo reentrega intacto.
/// </summary>
public sealed record ProcessOnboardingReceiptGenerationCommand(
    Guid GenerationId,
    string TemplateKey,
    int TemplateVersion,
    Guid OnboardingId,
    int DocumentVersion,
    string FileName,
    string CorrelationId,
    OnboardingReceiptPayload Receipt
);
