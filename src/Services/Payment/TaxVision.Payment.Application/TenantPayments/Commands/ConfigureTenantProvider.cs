using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.TenantPayments.Commands;

public sealed record ConfigureTenantProviderCommand(
    Guid TenantId,
    TenantPaymentProvider Provider,
    string? PublicKey,
    string? SecretKeyEncrypted,
    string? WebhookSecretEncrypted);

public static class ConfigureTenantProviderHandler
{
    public static async Task<Result> Handle(
        ConfigureTenantProviderCommand command,
        ITenantPaymentConfigRepository configs,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var existing = await configs.GetByTenantIdAsync(command.TenantId, ct);
        if (existing is null)
        {
            var newConfig = TenantPaymentConfig.Create(
                command.TenantId,
                command.Provider,
                command.PublicKey,
                command.SecretKeyEncrypted,
                command.WebhookSecretEncrypted);
            await configs.AddAsync(newConfig, ct);
        }
        else
        {
            existing.Configure(
                command.Provider,
                command.PublicKey,
                command.SecretKeyEncrypted,
                command.WebhookSecretEncrypted);
        }

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
