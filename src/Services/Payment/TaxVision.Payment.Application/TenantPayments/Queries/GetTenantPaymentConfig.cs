using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.TenantPayments.Queries;

public sealed record GetTenantPaymentConfigQuery(Guid TenantId);

public sealed record TenantPaymentConfigDto(
    Guid Id,
    Guid TenantId,
    TenantPaymentProvider Provider,
    bool IsActive,
    string? PublicKey);

public static class GetTenantPaymentConfigHandler
{
    public static async Task<Result<TenantPaymentConfigDto>> Handle(
        GetTenantPaymentConfigQuery query,
        ITenantPaymentConfigRepository configs,
        CancellationToken ct)
    {
        var config = await configs.GetByTenantIdAsync(query.TenantId, ct);
        if (config is null)
            return Result.Failure<TenantPaymentConfigDto>(
                new BuildingBlocks.Results.Error("TenantPayment.NotConfigured", "No payment configuration found for this tenant."));

        // Never return secret keys in responses
        return Result.Success(new TenantPaymentConfigDto(
            config.Id,
            config.TenantId,
            config.Provider,
            config.IsActive,
            config.PublicKey));
    }
}
