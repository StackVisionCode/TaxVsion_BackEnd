namespace BuildingBlocks.Messaging.PaymentClientIntegrationEvents;

/// <summary>Publicado cuando <c>account.updated</c>/<c>capability.updated</c> deja la cuenta
/// con requirements pendientes y todavía no habilitada para cobrar — Notification avisa al
/// tenant qué le falta completar en Stripe.</summary>
public sealed record TenantConnectAccountOnboardingRequiredIntegrationEvent : IntegrationEvent
{
    public required Guid TenantConnectAccountId { get; init; }
    public required IReadOnlyList<string> RequirementsCurrentlyDue { get; init; }
}

/// <summary>Publicado cuando la Connected Account queda lista para cobrar
/// (<c>ChargesEnabled=true</c> y cero requirements pendientes).</summary>
public sealed record TenantConnectAccountEnabledIntegrationEvent : IntegrationEvent
{
    public required Guid TenantConnectAccountId { get; init; }
}

/// <summary>Publicado cuando Stripe deshabilita los cobros de una cuenta previamente
/// habilitada (KYC/AML continuo, disputa, etc.) — requiere intervención del tenant.</summary>
public sealed record TenantConnectAccountRestrictedIntegrationEvent : IntegrationEvent
{
    public required Guid TenantConnectAccountId { get; init; }
    public required IReadOnlyList<string> RequirementsCurrentlyDue { get; init; }
}
