namespace TaxVision.Documents.Application.Generations.SaaSReceipt;

/// <summary>
/// Datos del recibo de una compra SaaS de un tenant que YA existe. A diferencia del recibo de onboarding,
/// acá sí hay oficina a quién facturarle, y el concepto no es un plan sino lo que se cobró (renovación,
/// asientos, complemento, mejora de plan…).
/// </summary>
public sealed record SaaSReceiptPayload(
    string OfficeName,
    string Description,
    long AmountPaidCents,
    string Currency,
    DateTime PaidAtUtc,
    string TransactionReferenceMask,
    /// <summary>Unidades y precio unitario. Cuando faltan, el recibo muestra solo el concepto y el total.</summary>
    int? Quantity = null,
    long? UnitAmountCents = null,
    /// <summary>Línea secundaria debajo del concepto (p.ej. "Additional seats for your subscription",
    /// "Prepaid wallet credit"). Cuando viene vacía, la plantilla cae a "{issuer.name} subscription"
    /// — el comportamiento legacy, válido para renovaciones y planes.</summary>
    string? SecondaryDescription = null
);

/// <summary>Registra la generación del recibo de una compra SaaS. Responde 202: el PDF se produce después.</summary>
public sealed record GenerateSaaSReceiptDocumentCommand(
    Guid TenantId,
    Guid SaaSPaymentId,
    string TemplateKey,
    int TemplateVersion,
    string SourceService,
    string IdempotencyKey,
    string CorrelationId,
    SaaSReceiptPayload Receipt
);

public sealed record GenerateSaaSReceiptDocumentResult(Guid GenerationId, string Status);

/// <summary>Comando local (cola durable) que genera el PDF. Transporta los datos, no los bytes.</summary>
public sealed record ProcessSaaSReceiptGenerationCommand(
    Guid GenerationId,
    Guid TenantId,
    Guid SaaSPaymentId,
    string TemplateKey,
    int TemplateVersion,
    string FileName,
    string CorrelationId,
    SaaSReceiptPayload Receipt
);
