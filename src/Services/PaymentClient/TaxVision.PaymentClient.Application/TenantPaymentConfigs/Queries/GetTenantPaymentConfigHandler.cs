using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

public static class GetTenantPaymentConfigHandler
{
    public static async Task<Result<TenantPaymentConfigResponse>> Handle(
        GetTenantPaymentConfigQuery query, ITenantPaymentConfigRepository configs, CancellationToken ct)
    {
        var config = await configs.GetByTenantAndProviderAsync(query.TenantId, query.ProviderCode, ct);
        if (config is null)
            return Result.Failure<TenantPaymentConfigResponse>(new Error("TenantPaymentConfig.NotFound", "TenantPaymentConfig does not exist."));

        return Result.Success(new TenantPaymentConfigResponse(
            config.Id,
            config.ProviderCode.ToString(),
            config.Mode.ToString(),
            config.PublishableKey,
            config.SecretKeyEncrypted is not null,
            config.WebhookSecretEncrypted is not null,
            config.StatementDescriptor.Value,
            config.IsActive,
            config.SettledAtUtc));
    }
}
