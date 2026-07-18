namespace TaxVision.Postmaster.Application.Suppression.Commands.RemoveSuppressionEntry;

public sealed record RemoveSuppressionEntryCommand(Guid TenantId, string Address);
