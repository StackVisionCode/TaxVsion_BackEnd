namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Credenciales YA descifradas de un tenant para un provider — a diferencia de PaymentApp
/// (config global vía <c>IOptions</c>), acá el mismo adapter atiende a todos los tenants, así
/// que las credenciales viajan por parámetro en cada llamada en vez de vivir inyectadas en
/// el constructor. El handler las descifra desde <c>TenantPaymentConfig</c> justo antes de
/// llamar al adapter — nunca se cachean en texto plano.
///
/// <para><paramref name="PublishableKey"/> y <paramref name="ApiBaseUrl"/> los usan providers cuyo
/// server-to-server necesita más que el secret: p.ej. PayPal hace OAuth con
/// <c>client-id:secret</c> (client-id = PublishableKey) contra <paramref name="ApiBaseUrl"/>
/// (sandbox vs live). Stripe los ignora (su SDK solo usa el secret).</para>
/// </summary>
public sealed record TenantProviderCredentials(
    string SecretKey,
    string? WebhookSecret,
    string? PublishableKey = null,
    string? ApiBaseUrl = null
);
