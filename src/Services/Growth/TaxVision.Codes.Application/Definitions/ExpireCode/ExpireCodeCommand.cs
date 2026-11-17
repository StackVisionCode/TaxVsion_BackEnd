namespace TaxVision.Codes.Application.Definitions.ExpireCode;

/// <summary>Cierra un código cuya fecha de fin ya pasó. Lo manda el barrido diario, uno por código.</summary>
public sealed record ExpireCodeCommand(Guid OwnerTenantId, Guid CodeDefinitionId);
