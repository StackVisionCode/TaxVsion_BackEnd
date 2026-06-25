using BuildingBlocks.Persistence;
using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using Wolverine;

namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record CreateTenantCommand(string Name, string Subdomain, string AdminEmail);

public sealed record TenantResponse(Guid Id, string Name, string Subdomain);

public static class CreateTenantHandler
{
    public static async Task<Result<TenantResponse>> Handle(
        CreateTenantCommand cmd,
        ITenantRepository repo,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        if (await repo.SubDomainExistsAsync(cmd.Subdomain, ct))
        {
            return Result.Failure<TenantResponse>(
                new Error("Tenant.Subdomain", "Subdomain already exists."));
        }

        var result = Domain.Tenant.Create(cmd.Name, cmd.Subdomain);
        if (result.IsFailure)
        {
            return Result.Failure<TenantResponse>(result.Error);
        }

        var tenant = result.Value;

        await repo.AddAsync(tenant, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new TenantCreatedIntegrationEvent
        {
            NewTenantId = tenant.Id,
            TenantId = tenant.Id,
            Name = tenant.Name,
            SubDomain = tenant.SubDomain,
            AdminEmail = cmd.AdminEmail
        });

        return Result.Success(
            new TenantResponse(tenant.Id, tenant.Name, tenant.SubDomain));
    }
}
