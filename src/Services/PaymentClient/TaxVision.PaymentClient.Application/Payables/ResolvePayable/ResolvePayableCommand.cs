namespace TaxVision.PaymentClient.Application.Payables.ResolvePayable;

/// <summary>Traduce la referencia estable pública a un token de checkout vigente. Crea el link de
/// forma perezosa si no hay ninguno Active para el payable (o el anterior expiró).</summary>
public sealed record ResolvePayableCommand(string Reference);

public sealed record ResolvePayableResponse(string CheckoutToken);
