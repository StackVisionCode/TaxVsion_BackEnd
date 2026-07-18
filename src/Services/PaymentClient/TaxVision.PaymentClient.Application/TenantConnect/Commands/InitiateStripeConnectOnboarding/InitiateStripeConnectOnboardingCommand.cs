using TaxVision.PaymentClient.Domain.Connect;

namespace TaxVision.PaymentClient.Application.TenantConnect.Commands.InitiateStripeConnectOnboarding;

/// <summary><see cref="Email"/> es el del TenantAdmin autenticado que dispara el onboarding —
/// PaymentClient no persiste un email de contacto en su proyección local de Tenant, así que
/// viaja explícito en cada llamada.</summary>
public sealed record InitiateStripeConnectOnboardingCommand(
    Guid TenantId,
    ConnectAccountType Type,
    string Email,
    string RefreshUrl,
    string ReturnUrl,
    Guid ActorUserId
);
