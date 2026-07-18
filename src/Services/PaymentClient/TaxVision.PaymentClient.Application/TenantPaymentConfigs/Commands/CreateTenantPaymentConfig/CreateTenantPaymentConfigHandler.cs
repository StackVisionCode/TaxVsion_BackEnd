using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Common;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.CreateTenantPaymentConfig;

public static class CreateTenantPaymentConfigHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateTenantPaymentConfigCommand command,
        ITenantPaymentConfigRepository configs,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var existing = await configs.GetByTenantAndProviderAsync(command.TenantId, command.ProviderCode, ct);
        if (existing is not null)
            return Result.Failure<Guid>(
                new Error("TenantPaymentConfig.AlreadyExists", "A config for this tenant and provider already exists.")
            );

        var descriptorResult = StatementDescriptor.Create(command.StatementDescriptor);
        if (descriptorResult.IsFailure)
            return Result.Failure<Guid>(descriptorResult.Error);

        var nowUtc = DateTime.UtcNow;
        var createResult = TenantPaymentConfig.Create(
            command.TenantId,
            command.ProviderCode,
            command.Mode,
            command.PublishableKey,
            descriptorResult.Value,
            nowUtc
        );
        if (createResult.IsFailure)
            return Result.Failure<Guid>(createResult.Error);

        var config = createResult.Value;
        await configs.AddAsync(config, ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            nameof(TenantPaymentConfig),
            config.Id,
            PaymentAuditAction.TenantPaymentConfigCreated,
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                command.ProviderCode,
                command.Mode,
                config.PublishableKey,
            },
            reason: null,
            nowUtc,
            ct
        );

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(config.Id);
    }
}
