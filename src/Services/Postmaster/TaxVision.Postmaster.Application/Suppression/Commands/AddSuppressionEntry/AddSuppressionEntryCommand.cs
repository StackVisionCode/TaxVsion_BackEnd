using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Application.Suppression.Commands.AddSuppressionEntry;

public sealed record AddSuppressionEntryCommand(
    Guid TenantId,
    string Address,
    SuppressionReason Reason,
    Guid? AddedByUserId,
    string? Notes
);
