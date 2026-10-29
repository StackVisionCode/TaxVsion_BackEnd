namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

public sealed record RetryFormCustomerLinkCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    DateTime NowUtc
);
