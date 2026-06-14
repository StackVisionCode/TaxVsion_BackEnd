using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.IntegrationEvents;
using Wolverine;
namespace TaxVision.Tenant.Application.Tenants.Commands;

public sealed record CreateTenantCommand(string Name, string Subdomain, string AdminEmail);
public sealed record TenantResponse(Guid Id, string Name, string Subdomain);


public static class CreateTenantHandler
{
    public static async Task<Result<TenantResponse>> Handle(
    CreateTenantCommand cmd,
    ITenantRepository repo,
    IMessageBus bus, // bus de Wolverine para publicar
    CancellationToken ct)
    {
        // 1) Regla que cruza la persistencia: subdominio único.
        if (await repo.SubDomainExistsAsync(cmd.Subdomain, ct))
            return Result.Failure<TenantResponse>(new Error("Tenant.Subdomain", "Subdomain already exists."));

        // 2) Crear el aggregate vía su fábrica (valida formato e invariantes).
        var result = Domain.Tenant.Create(cmd.Name, cmd.Subdomain);
        if (result.IsFailure)
            return Result.Failure<TenantResponse>(result.Error);
        var tenant = result.Value;
        // 3) Persistir. (El AddAsync registra; el commit lo hace Wolverine con su transacción + Outbox: ver sección 9.)
        await repo.AddAsync(tenant, ct);
        // // 4) Publicar el evento de integración → dispara la coreografía.
        // // Wolverine lo mete en el Outbox dentro de la misma transacción.
        await bus.PublishAsync(new TenantCreatedIntegrationEvent
        {
            NewTenantId = tenant.Id,
            TenantId = tenant.Id,
            Name = tenant.Name,
            SubDomain = tenant.SubDomain,
            AdminEmail = cmd.AdminEmail
        });
        // // 5) Devolver la respuesta.
        return Result.Success(
        new TenantResponse(tenant.Id, tenant.Name, tenant.SubDomain));
    }

}
