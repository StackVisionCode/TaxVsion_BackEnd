using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Requests;

/// <summary>Hecho interno emitido cuando un firmante completa sus campos de un documento.</summary>
public sealed record SignerCompletedDocument(Guid SignerId, Guid DocumentId, DateTime OccurredAtUtc) : IDomainEvent;
