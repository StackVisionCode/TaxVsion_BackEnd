namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

/// <summary>
/// Publicado por Customer Service cuando un job de bulk import termina (Completed o Canceled).
/// Un solo evento batched por job, no uno por customer. Lotes acotados segun la guia del senior.
///
/// NO incluye PII: solo IDs. Los consumidores que necesitan datos deben hacer GET /customers/{id}
/// o consumir CustomerCreatedV1 individual por cada CreatedCustomerId.
///
/// Consumidores deben ser idempotentes: el mismo ImportJobId puede reentregarse por outbox.
/// </summary>
public sealed record CustomersBulkImportedIntegrationEvent : IntegrationEvent
{
    public required Guid ImportJobId { get; init; }
    public required Guid CreatedByUserId { get; init; }
    public required DateTime CompletedAtUtc { get; init; }
    public required int TotalRows { get; init; }
    public required int SuccessCount { get; init; }
    public required int UpdatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int FailedCount { get; init; }

    /// <summary>Ids de customers nuevos creados en el job.</summary>
    public required IReadOnlyList<Guid> CreatedCustomerIds { get; init; }

    /// <summary>Ids de customers actualizados (estrategia Merge u Overwrite con duplicado).</summary>
    public required IReadOnlyList<Guid> UpdatedCustomerIds { get; init; }
}
