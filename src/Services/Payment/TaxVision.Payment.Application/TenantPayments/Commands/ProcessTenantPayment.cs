using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;

namespace TaxVision.Payment.Application.TenantPayments.Commands;

public sealed record ProcessTenantPaymentCommand(
    Guid TenantId,
    Guid? CustomerId,
    long AmountCents,
    string Currency,
    string Description);

public sealed record ProcessTenantPaymentResponse(
    Guid TransactionId,
    string Status,
    string? ExternalTransactionId);

public static class ProcessTenantPaymentHandler
{
    public static async Task<Result<ProcessTenantPaymentResponse>> Handle(
        ProcessTenantPaymentCommand command,
        ITenantPaymentConfigRepository configs,
        ITenantTransactionRepository transactions,
        IPaymentAdapterFactory adapterFactory,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var config = await configs.GetByTenantIdAsync(command.TenantId, ct);
        if (config is null || !config.IsActive)
            return Result.Failure<ProcessTenantPaymentResponse>(
                new BuildingBlocks.Results.Error("TenantPayment.NotConfigured", "No active payment configuration found for this tenant."));

        if (string.IsNullOrWhiteSpace(config.SecretKeyEncrypted))
            return Result.Failure<ProcessTenantPaymentResponse>(
                new BuildingBlocks.Results.Error("TenantPayment.MissingSecretKey", "Payment provider secret key is not configured."));

        var transaction = TenantTransaction.Create(
            command.TenantId,
            command.CustomerId,
            config.Provider,
            command.AmountCents,
            command.Currency,
            command.Description);
        await transactions.AddAsync(transaction, ct);
        await uow.SaveChangesAsync(ct);

        var adapter = adapterFactory.GetAdapter(config.Provider);
        var result = await adapter.ChargeAsync(config.SecretKeyEncrypted, command.AmountCents, command.Currency, command.Description, ct);

        if (result.IsSuccess && result.ExternalTransactionId is not null)
            transaction.MarkCompleted(result.ExternalTransactionId);
        else
            transaction.MarkFailed(result.FailureReason ?? "Payment failed.");

        await uow.SaveChangesAsync(ct);

        return Result.Success(new ProcessTenantPaymentResponse(
            transaction.Id,
            transaction.Status.ToString(),
            transaction.ExternalTransactionId));
    }
}
