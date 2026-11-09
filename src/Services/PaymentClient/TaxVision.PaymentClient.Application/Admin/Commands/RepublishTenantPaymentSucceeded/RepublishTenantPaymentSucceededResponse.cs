namespace TaxVision.PaymentClient.Application.Admin.Commands.RepublishTenantPaymentSucceeded;

/// <summary><see cref="Republished"/>: lo republicado, o lo que se republicaría si es simulación.</summary>
public sealed record RepublishTenantPaymentSucceededResponse(
    bool DryRun,
    IReadOnlyList<Guid> Republished,
    IReadOnlyList<SkippedTenantPayment> Skipped
);

public sealed record SkippedTenantPayment(Guid TenantPaymentId, string Reason);
