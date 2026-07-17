using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using Wolverine;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A4 — "encuentra tu oficina" estilo Slack: dado un email, envía por correo los
/// subdominios donde tiene cuenta activa. Nunca revela en la respuesta HTTP si el
/// email existe o cuántas oficinas encontró (anti-enumeración) — siempre éxito.
/// </summary>
public sealed record RequestTenantRecoveryCommand(string Email);

public static class RequestTenantRecoveryHandler
{
    public static async Task<Result> Handle(
        RequestTenantRecoveryCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        ITenantDomainRepository domains,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var email = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(email))
            return Result.Success();

        var tenantIds = await users.GetActiveTenantIdsByEmailAsync(email, ct);
        if (tenantIds.Count == 0)
            return Result.Success();

        var matches = new List<TenantRecoveryMatch>();
        foreach (var tenantId in tenantIds)
        {
            var tenant = await tenants.GetByIdAsync(tenantId, ct);
            if (tenant is null || !tenant.IsActive)
                continue;

            var tenantDomains = await domains.GetByTenantAsync(tenantId, ct);
            var primary = tenantDomains.FirstOrDefault(domain =>
                domain.IsPrimary && domain.Status == TenantDomainStatus.Active
            );
            if (primary is null)
                continue; // sin dominio activo no hay enlace útil que mandar

            matches.Add(new TenantRecoveryMatch(tenant.Name, primary.Host));
        }

        if (matches.Count == 0)
            return Result.Success();

        await bus.PublishAsync(
            new TenantRecoveryRequestedIntegrationEvent
            {
                // Cruza tenants por diseño: no pertenece a uno solo. Se marca como
                // notificación de plataforma (NotificationLog.Create rechaza Guid.Empty).
                TenantId = PlatformTenant.Id,
                Email = email,
                Matches = matches,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
