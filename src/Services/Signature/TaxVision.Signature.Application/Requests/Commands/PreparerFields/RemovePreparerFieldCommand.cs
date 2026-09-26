namespace TaxVision.Signature.Application.Requests.Commands.PreparerFields;

/// <summary>Quita un campo del preparador (Draft/Ready).</summary>
public sealed record RemovePreparerFieldCommand(Guid TenantId, Guid SignatureRequestId, Guid FieldId);
