using BuildingBlocks.Results;

namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — verifica el token de firma HMAC emitido por <see cref="IPurchaseIntentAmountSigner"/>.
/// Códigos de error que puede retornar:
/// <list type="bullet">
///   <item><c>PurchaseIntent.AmountSignature.Missing</c> — el request no trae firma y <c>RejectUnsigned</c> está activo</item>
///   <item><c>PurchaseIntent.AmountSignature.VersionUnsupported</c> — prefijo no reconocido (futura rotación de formato)</item>
///   <item><c>PurchaseIntent.AmountSignature.Malformed</c> — formato roto (no son 3 partes, base64 inválido, etc.)</item>
///   <item><c>PurchaseIntent.AmountSignature.Expired</c> — <c>signedAt</c> está fuera de la ventana <c>ClockSkew</c></item>
///   <item><c>PurchaseIntent.AmountSignature.Invalid</c> — hmac no matchea ni con <c>Secret</c> ni con <c>PreviousSecret</c>; el amount/currency/intentId/tenantId que se firmó NO coincide con el que llegó por body</item>
/// </list>
/// <c>Success()</c> cuando la firma matchea, o cuando no hay firma y <c>RejectUnsigned=false</c> (pass-through log-only).</summary>
public interface IPurchaseIntentAmountVerifier
{
    Result Verify(PurchaseIntentAmountInput input, string? signature);
}
