using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.Connect;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>Snapshot de <c>account.updated</c>/<c>capability.updated</c> ya traducido — el
/// Domain nunca ve el payload crudo de Stripe (guardrail §44.1).</summary>
public sealed record ConnectAccountStatusSnapshot(bool ChargesEnabled, bool PayoutsEnabled, IReadOnlyList<string> RequirementsCurrentlyDue);

/// <summary>
/// Evento del webhook de plataforma (<c>/payments-client/webhooks/stripe-connect</c>) ya
/// verificado y traducido. Los campos de cuenta (<see cref="ChargesEnabled"/>,
/// <see cref="PayoutsEnabled"/>, <see cref="RequirementsCurrentlyDue"/>) solo se completan
/// para <c>account.updated</c>/<c>capability.updated</c>; los de payout
/// (<see cref="PayoutReference"/>, <see cref="PayoutAmountCents"/>, etc.) solo para
/// <c>payout.paid</c>/<c>payout.failed</c> — el handler decide cuál rama leer según
/// <see cref="EventType"/>.
/// </summary>
public sealed record ConnectWebhookEvent(
    string ProviderEventId,
    string EventType,
    string StripeConnectAccountId,
    bool? ChargesEnabled,
    bool? PayoutsEnabled,
    IReadOnlyList<string>? RequirementsCurrentlyDue,
    string? PayoutReference,
    long? PayoutAmountCents,
    string? PayoutCurrency,
    string? PayoutFailureReason);

/// <summary>
/// Operaciones de plataforma sobre Connected Accounts — a diferencia de
/// <see cref="IPaymentProvider"/> (que cobra en nombre de un tenant), esto siempre corre con
/// las credenciales de la PLATAFORMA (crear la cuenta, generar el link de onboarding,
/// consultar su estado). Solo Stripe soporta Connect en el catálogo actual — no hay factory
/// keyed como con <see cref="IPaymentAdapterFactory"/> porque no hace falta todavía.
/// </summary>
public interface IStripeConnectGateway
{
    Task<Result<string>> CreateAccountAsync(ConnectAccountType type, string tenantEmail, CancellationToken ct);

    Task<Result<string>> CreateOnboardingLinkAsync(string stripeConnectAccountId, string refreshUrl, string returnUrl, CancellationToken ct);

    Task<Result<ConnectAccountStatusSnapshot>> GetAccountStatusAsync(string stripeConnectAccountId, CancellationToken ct);

    /// <summary>Verifica la firma HMAC contra <see cref="PlatformStripeCredentials.ConnectWebhookSecret"/>
    /// (secret de plataforma — distinto del <c>WebhookSecretEncrypted</c> per-tenant que usa
    /// el webhook de <c>TenantPayment</c>) y traduce el payload ya autenticado.</summary>
    Task<Result<ConnectWebhookEvent>> VerifyAndParseConnectWebhookAsync(string rawPayload, string signatureHeader, string webhookSecret, CancellationToken ct);
}
