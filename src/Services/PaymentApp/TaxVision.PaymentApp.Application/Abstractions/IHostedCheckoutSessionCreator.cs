using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// Abstracción provider-agnostic para crear una sesión de checkout hosteada. A diferencia de
/// <c>IPaymentProvider.CreateHostedCheckoutSessionAsync</c>, no acopla al dominio los value
/// objects internos del adapter (Money, StatementDescriptor, PaymentMethodKind) y expone un
/// único flag <see cref="HostedCheckoutCreateRequest.StoreMethodForOffSession"/> que cada
/// provider traduce a su equivalente (Stripe: <c>SetupFutureUsage</c>; PayPal: <c>Vault</c>).
/// </summary>
public interface IHostedCheckoutSessionCreator
{
    /// <summary>Código del provider — permite al factory indexar varios creators y al handler
    /// elegir el correcto cuando el command trae un provider específico.</summary>
    string ProviderCode { get; }

    Task<Result<HostedCheckoutSession>> CreateAsync(HostedCheckoutCreateRequest request, CancellationToken ct);
}

/// <summary>Indexa los <see cref="IHostedCheckoutSessionCreator"/> por su <see cref="IHostedCheckoutSessionCreator.ProviderCode"/>.
/// Molde: <c>OffSessionChargerFactory</c>. Devuelve null cuando el provider pedido no está registrado.</summary>
public interface IHostedCheckoutSessionCreatorFactory
{
    IHostedCheckoutSessionCreator? For(string? providerCode);
}

/// <summary>
/// Intención provider-agnostic de abrir un checkout hosteado. <paramref name="Provider"/> es
/// opcional: null deja al factory elegir el default configurado. <paramref name="Method"/>
/// también opcional — algunos providers autodetectan según el país del pagador.
/// <paramref name="StoreMethodForOffSession"/> es la pieza clave de la auto-renewal: cuando
/// true, el adapter DEBE configurar el provider para que tras pagar, el método quede guardado
/// para cobro off-session; el UI del landing lo setea true cuando el usuario eligió auto-renew.
/// <paramref name="IntentType"/> decide si el checkout cobra (<c>Payment</c>, default) o solo
/// captura un método para uso off-session (<c>Setup</c>, Stripe mode="setup"): en Setup no se
/// usa <see cref="AmountCents"/>/<see cref="SaaSPaymentId"/> y el adapter exige
/// <see cref="StoreMethodForOffSession"/>=true.
/// </summary>
public sealed record HostedCheckoutCreateRequest(
    Guid TenantId,
    Guid SaaSPaymentId,
    long AmountCents,
    string Currency,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    string IdempotencyKey,
    string? Provider,
    string? Method,
    bool StoreMethodForOffSession,
    HostedCheckoutIntentType IntentType = HostedCheckoutIntentType.Payment
);

/// <summary>Payment cobra en el checkout; Setup solo captura/guarda un método (sin cobro).</summary>
public enum HostedCheckoutIntentType
{
    Payment = 0,
    Setup = 1,
}

public sealed record HostedCheckoutSession(Guid PaymentId, string CheckoutUrl, DateTime ExpiresAtUtc);
