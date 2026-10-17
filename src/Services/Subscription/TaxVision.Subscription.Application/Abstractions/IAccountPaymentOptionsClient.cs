using BuildingBlocks.Results;

namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>
/// Un método de pago disponible para el tenant en el Account del Landing (add-ons, seats, renewal, plan
/// change). PaymentApp es la fuente de verdad del catálogo: el landing lee desde acá y pinta el picker;
/// el checkout del backend sigue validando contra el mismo catálogo.
/// </summary>
public sealed record AccountPaymentOption(
    string Provider,
    string Method,
    string DisplayName,
    bool Enabled,
    int Priority,
    string? DisabledReason
);

public sealed record AccountPaymentOptionsResult(IReadOnlyList<AccountPaymentOption> Options);

/// <summary>
/// Lista los métodos de pago habilitados para que el landing pregunte al usuario (addons/seats/renewal/
/// plan-change). Hoy el landing enviaba checkouts sin provider y el backend asumía Stripe; esto rompía a
/// cualquier tenant que onboardeó con PayPal. Reusa el catálogo operativo de PaymentApp vía M2M.
/// </summary>
public interface IAccountPaymentOptionsClient
{
    Task<Result<AccountPaymentOptionsResult>> GetOptionsAsync(Guid tenantId, CancellationToken ct = default);
}
