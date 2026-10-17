using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.PurchaseIntentAmount;

/// <summary>SEC-M1.C — un único registration-call por servicio. Subscription y Wallet llaman al
/// signer; PaymentApp llama al verifier. Los 3 bindean la misma sección de config
/// (<see cref="PurchaseIntentAmountSignatureOptions.SectionName"/>) — misma secret en los 3 lados
/// o la verificación falla con <c>AmountSignature.Invalid</c>.</summary>
public static class PurchaseIntentAmountServiceCollectionExtensions
{
    public static IServiceCollection AddPurchaseIntentAmountSigning(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        BindOptions(services, configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPurchaseIntentAmountSigner, HmacPurchaseIntentAmountSigner>();
        return services;
    }

    public static IServiceCollection AddPurchaseIntentAmountVerification(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        BindOptions(services, configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPurchaseIntentAmountVerifier, HmacPurchaseIntentAmountVerifier>();
        return services;
    }

    private static void BindOptions(IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<PurchaseIntentAmountSignatureOptions>()
            .Bind(configuration.GetSection(PurchaseIntentAmountSignatureOptions.SectionName));
    }
}
