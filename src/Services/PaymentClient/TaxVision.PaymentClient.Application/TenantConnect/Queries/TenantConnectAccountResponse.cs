namespace TaxVision.PaymentClient.Application.TenantConnect.Queries;

public sealed record TenantConnectAccountResponse(
    Guid Id,
    string AccountType,
    string Status,
    string OnboardingStep,
    bool CanCharge,
    bool CanReceivePayouts,
    IReadOnlyList<string> RequirementsCurrentlyDue
);
