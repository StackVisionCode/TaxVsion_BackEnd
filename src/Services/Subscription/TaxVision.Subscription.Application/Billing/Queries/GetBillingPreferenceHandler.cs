using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Billing;

namespace TaxVision.Subscription.Application.Billing.Queries;

/// <summary>Lee la preferencia tal cual la ve Subscription (fuente de verdad). Si no hay registro
/// aún, devuelve el default Manual — el tenant simplemente no eligió.</summary>
public static class GetBillingPreferenceHandler
{
    public static async Task<Result<BillingPreferenceResponse>> Handle(
        GetBillingPreferenceQuery query,
        ITenantBillingPreferenceRepository repository,
        CancellationToken ct
    )
    {
        var preference = await repository.GetByTenantIdAsync(query.TenantId, ct);
        if (preference is null)
            return Result.Success(
                new BillingPreferenceResponse(
                    Mode: BillingMode.Manual.ToString(),
                    ProviderCode: null,
                    HasActiveMandate: false,
                    AcceptedAtUtc: null,
                    AgreedAmountCents: null,
                    AgreedCurrency: null,
                    AgreedCadence: null,
                    ConsentTextVersion: null
                )
            );

        var active = preference.ActiveMandate();
        return Result.Success(
            new BillingPreferenceResponse(
                Mode: preference.Mode.ToString(),
                ProviderCode: preference.ProviderCode,
                HasActiveMandate: active is not null,
                AcceptedAtUtc: active?.AcceptedAtUtc,
                AgreedAmountCents: active?.AgreedAmountCents,
                AgreedCurrency: active?.AgreedCurrency,
                AgreedCadence: active?.AgreedCadence,
                ConsentTextVersion: active?.ConsentTextVersion
            )
        );
    }
}
