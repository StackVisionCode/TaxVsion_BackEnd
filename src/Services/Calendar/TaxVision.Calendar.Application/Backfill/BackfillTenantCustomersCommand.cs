using BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Calendar.Application.Backfill;

/// <summary>
/// Siembra el directorio de customers la primera vez que Calendar ve un tenant. No hay endpoint que enumere tenants:
/// lo devuelven en cascada los consumers de Customer, y la cola local durable lo guarda con la transacción del evento.
/// </summary>
public sealed record BackfillTenantCustomersCommand(Guid TenantId)
{
    /// <summary>El comando si el tenant no se ha sembrado, o <c>null</c> para no publicar nada.</summary>
    public static async Task<BackfillTenantCustomersCommand?> IfMissingAsync(
        Guid tenantId,
        ITenantBackfillStateRepository states,
        CancellationToken ct
    ) => await states.GetByTenantIdAsync(tenantId, ct) is null ? new BackfillTenantCustomersCommand(tenantId) : null;
}

/// <summary>
/// Pagina Customer sin transacción alrededor: cada página se guarda en la suya con
/// <see cref="SaveCustomerDirectoryPageCommand"/>. Si una falla no queda el estado hecho, y el reintento rehace todo
/// sin duplicar, porque guardar es un upsert por cliente.
/// </summary>
public static class BackfillTenantCustomersHandler
{
    private const int PageSize = 100;

    [NonTransactional]
    public static async Task Handle(
        BackfillTenantCustomersCommand command,
        ITenantBackfillStateRepository states,
        ICalendarCustomerClient customers,
        IMessageBus bus,
        ILogger<BackfillTenantCustomersCommand> logger,
        CancellationToken ct
    )
    {
        if (await states.GetByTenantIdAsync(command.TenantId, ct) is not null)
            return;

        for (var page = 1; ; page++)
        {
            // La hora es de antes de leer: un evento posterior a la lectura tiene que poder pisar esta foto.
            var readAtUtc = DateTime.UtcNow;
            var result =
                await customers.ListActiveCustomersAsync(command.TenantId, page, PageSize, ct)
                ?? throw new DependencyNotReadyException("Customer", $"listing page {page} failed");

            await bus.InvokeAsync(
                new SaveCustomerDirectoryPageCommand(command.TenantId, result.Items, readAtUtc, !result.HasMore),
                ct
            );

            if (!result.HasMore)
                break;
        }

        logger.LogInformation("Customer directory backfill completed for tenant {TenantId}.", command.TenantId);
    }
}
