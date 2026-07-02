namespace TaxVision.Customer.Application.Customers.Commands.BulkChangeStatus;

public enum BulkStatusAction
{
    Archive,
    Reactivate,
    Activate,
    Deactivate,
}

public sealed record BulkChangeStatusCommand(
    Guid TenantId,
    Guid ModifiedByUserId,
    BulkStatusAction Action,
    IReadOnlyList<Guid> CustomerIds,
    string? Reason
);

public sealed record BulkStatusActionResponse(
    int TotalRequested,
    int Succeeded,
    int Failed,
    IReadOnlyList<BulkFailedItem> Failures
);

public sealed record BulkFailedItem(Guid CustomerId, string ErrorCode, string Message);
