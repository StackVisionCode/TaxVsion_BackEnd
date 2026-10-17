using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.OnboardingPaymentOptions;

namespace TaxVision.PaymentApp.Application.AccountPaymentOptions;

/// <summary>
/// Lista los metodos de pago operativos del catalogo para que el Account del Landing pinte el picker
/// antes de arrancar cualquier compra manual (add-on, seats, renewal, plan change). A diferencia de
/// <see cref="GetOnboardingPaymentOptionsQuery"/>, no toma planId: el tenant ya existe y el guard de
/// metodo real lo aplica cada checkout especifico server-side.
/// </summary>
public sealed record GetAccountPaymentOptionsQuery();

/// <summary>Mismo shape que <see cref="OnboardingPaymentOptionsResponse"/> por paridad del picker; se
/// deja un record propio para no acoplar los contratos (puede divergir si en el futuro Account filtra
/// por reglas distintas al onboarding).</summary>
public sealed record AccountPaymentOptionsResponse(IReadOnlyList<OnboardingPaymentOption> Options);

public static class GetAccountPaymentOptionsHandler
{
    public static async Task<Result<AccountPaymentOptionsResponse>> Handle(
        GetAccountPaymentOptionsQuery _,
        IOnboardingPaymentMethodCatalog catalog,
        CancellationToken ct
    )
    {
        var options = await catalog.GetOperationalOptionsAsync(ct);
        return options.IsSuccess
            ? Result.Success(new AccountPaymentOptionsResponse(options.Value))
            : Result.Failure<AccountPaymentOptionsResponse>(options.Error);
    }
}
