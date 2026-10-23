namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

public sealed record RetryFormActionRunCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    Guid RunId,
    DateTime NowUtc
);
