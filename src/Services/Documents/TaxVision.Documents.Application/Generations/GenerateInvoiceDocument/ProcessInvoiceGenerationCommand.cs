namespace TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

/// <summary>
/// Comando local (cola durable de Wolverine) que ejecuta la generación real de forma asíncrona,
/// fuera de la llamada HTTP que devolvió 202. Lo publica <see cref="GenerateInvoiceDocumentHandler"/>
/// dentro de la misma transacción que persiste la generación Requested, así que se entrega recién al
/// commitear (outbox durable). Transporta los DATOS de la factura (no bytes) para poder renderizar en
/// el nuevo scope; si el mensaje se reintenta, Wolverine lo reentrega con los datos intactos.
/// </summary>
public sealed record ProcessInvoiceGenerationCommand(
    Guid GenerationId,
    Guid TenantId,
    string InvoiceNumber,
    string TemplateKey,
    int TemplateVersion,
    string OwnerType,
    Guid OwnerId,
    int DocumentVersion,
    int TaxYear,
    string FileName,
    string CorrelationId,
    InvoicePayload Invoice,
    BrandingPayload? Branding = null
);
