using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CustomerIntegrationEvents;

/// <summary>
/// Un bloque del import (hasta 500 filas) quedó guardado. Sale en la misma transacción que sus clientes (outbox): si
/// el bloque se confirma, su aviso también. Lleva la ficha mínima de directorio de cada cliente creado o actualizado,
/// para que nadie tenga que pedirla por HTTP. Nunca lleva SSN/EIN ni datos fiscales.
///
/// <see cref="CustomersBulkImportedIntegrationEvent"/> sigue marcando el fin del trabajo (aviso al usuario).
/// Idempotente por (ImportJobId, ChunkNumber): un reintento del mismo bloque repite los mismos clientes.
/// </summary>
[MessageIdentity("customer.import_chunk_committed.v1")]
public sealed record CustomersImportChunkCommittedIntegrationEvent : IntegrationEvent
{
    public required Guid ImportJobId { get; init; }

    /// <summary>Posición del bloque en el archivo (1, 2, …). Estable entre reintentos.</summary>
    public required int ChunkNumber { get; init; }

    public required Guid CreatedByUserId { get; init; }
    public required IReadOnlyList<ImportedCustomerCard> Customers { get; init; }
}

/// <summary>Ficha de directorio de un cliente importado: lo mismo que viaja en <c>CustomerCreated</c>.</summary>
public sealed record ImportedCustomerCard
{
    public required Guid CustomerId { get; init; }

    /// <summary>true = creado por el import; false = existente actualizado (Merge/Overwrite).</summary>
    public required bool IsNew { get; init; }

    public required string Kind { get; init; } // "Individual" | "Business"
    public required string DisplayName { get; init; }

    /// <summary>Nullable desde el contrato: el email será opcional (D-3).</summary>
    public string? PrimaryEmail { get; init; }

    public string? PrimaryPhone { get; init; }
    public required string Language { get; init; } // "Es" | "En"
    public required string Status { get; init; }
    public bool EmailEnabled { get; init; } = true;
    public bool SmsEnabled { get; init; } = true;
    public Guid? OccupationId { get; init; }
    public Guid? AvatarFileId { get; init; }
}
