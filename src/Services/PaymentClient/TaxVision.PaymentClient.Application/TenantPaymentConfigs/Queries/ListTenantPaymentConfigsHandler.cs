using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

public static class ListTenantPaymentConfigsHandler
{
    public static async Task<Result<IReadOnlyList<TenantPaymentConfigResponse>>> Handle(
        ListTenantPaymentConfigsQuery query,
        ITenantPaymentConfigRepository configs,
        CancellationToken ct
    )
    {
        var all = await configs.GetAllByTenantAsync(query.TenantId, ct);

        IReadOnlyList<TenantPaymentConfigResponse> response = all.Select(config => new TenantPaymentConfigResponse(
                config.Id,
                config.ProviderCode.ToString(),
                config.Mode.ToString(),
                config.PublishableKey,
                config.SecretKeyEncrypted is not null,
                config.WebhookSecretEncrypted is not null,
                config.StatementDescriptor.Value,
                config.IsActive,
                config.SettledAtUtc
            ))
            .ToList();

        return Result.Success(response);
    }
}
