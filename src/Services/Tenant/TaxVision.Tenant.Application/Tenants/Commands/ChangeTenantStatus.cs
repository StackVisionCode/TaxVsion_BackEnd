using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    EnumTenantStatus.TenantStatus Status);

public static class ChangeTenantStatusHandler
{
    public static async Task<Result> Handle(
        ChangeTenantStatusCommand command,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null)
            return Result.Failure(new Error("Tenant.NotFound", "Tenant does not exist."));

        var change = tenant.ChangeStatus(command.Status);
        if (change.IsFailure)
            return change;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new TenantStatusChangedIntegrationEvent
        {
            ChangedTenantId = tenant.Id,
            TenantId = tenant.Id,
            Status = tenant.Status.ToString(),
            IsActive = tenant.Status == EnumTenantStatus.TenantStatus.Active,
            CorrelationId = correlation.CorrelationId
        });

        return Result.Success();
    }
}
