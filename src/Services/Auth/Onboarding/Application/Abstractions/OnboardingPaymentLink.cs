namespace TaxVision.Auth.Onboarding.Application.Abstractions;

/// <summary>Alta pagada con su oficina ya creada y, si existe, el recibo que Auth le generó.</summary>
public sealed record OnboardingPaymentLink(Guid OnboardingId, Guid TenantId, Guid? ReceiptFileId);
