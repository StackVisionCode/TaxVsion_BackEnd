using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Backfill;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Correspondence.Application.Backfill;

/// <summary>Trae la siguiente página de clientes de un tenant (cola local durable y secuencial).</summary>
public sealed record BackfillTenantCustomersCommand(Guid TenantId);

/// <summary>
/// Una página por transacción (COR-08): Customer se llama sin transacción abierta y el avance queda en
/// <see cref="TenantBackfillState"/>, así un reintento sigue desde la última página confirmada. Si hay más, encola
/// la siguiente. Un comando repetido solo avanza una página: termina en cuanto el tenant queda completo.
/// </summary>
public static class BackfillTenantCustomersHandler
{
    private const int PageSize = 100;

    [NonTransactional]
    public static async Task Handle(
        BackfillTenantCustomersCommand command,
        ITenantBackfillStateRepository states,
        ICustomerEmailAddressRepository emails,
        ICorrespondenceCustomerClient customerClient,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<BackfillTenantCustomersCommand> logger,
        CancellationToken ct
    )
    {
        var tenantId = command.TenantId;
        var state = await states.GetByTenantIdAsync(tenantId, ct);
        if (state is { IsCompleted: true })
            return;

        var page = (state?.LastConfirmedPage ?? 0) + 1;
        var result =
            await customerClient.ListActiveCustomersAsync(tenantId, page, PageSize, ct)
            ?? throw new DependencyNotReadyException("Customer.Api", $"Backfill page {page} is not available yet.");

        if (state is null)
        {
            state = TenantBackfillState.Start(tenantId);
            await states.AddAsync(state, ct);
        }

        var seenEmails = new HashSet<string>(StringComparer.Ordinal);
        var seeded = new List<LinkUnlinkedMailCommand>();
        foreach (var customer in result.Items)
        {
            var address = await SeedCustomerAsync(tenantId, customer, seenEmails, emails, logger, ct);
            if (address is not null)
                seeded.Add(new LinkUnlinkedMailCommand(tenantId, customer.Id, address));
        }

        state.ConfirmPage(page, isLastPage: !result.HasMore);
        await unitOfWork.SaveChangesAsync(ct);

        foreach (var link in seeded)
            await bus.PublishAsync(link);
        if (result.HasMore)
            await bus.PublishAsync(new BackfillTenantCustomersCommand(tenantId));
        else
            logger.LogInformation("Customer email backfill completed for tenant {TenantId}.", tenantId);
    }

    // Devuelve la dirección si entró a la proyección.
    private static async Task<string?> SeedCustomerAsync(
        Guid tenantId,
        RemoteCustomerSummary customer,
        HashSet<string> seenEmails,
        ICustomerEmailAddressRepository emails,
        ILogger logger,
        CancellationToken ct
    )
    {
        // El listado ya pide status=Active del lado de Customer.Api: esto es defensa en profundidad.
        if (!customer.IsActive)
            return null;

        var emailResult = EmailAddress.Create(customer.PrimaryEmail);
        if (emailResult.IsFailure)
        {
            logger.LogWarning(
                "Customer {CustomerId} for tenant {TenantId} has an invalid PrimaryEmail; skipped in backfill.",
                customer.Id,
                tenantId
            );
            return null;
        }

        var existing = await emails.GetByCustomerIdAsync(tenantId, customer.Id, ct);
        if (existing is not null)
            return null;

        // Dos clientes pueden compartir email: dentro de la página el segundo aún no está en la base, de ahí el set.
        if (!seenEmails.Add(emailResult.Value.NormalizedValue))
        {
            logger.LogWarning(
                "Customer {CustomerId} for tenant {TenantId} shares an email with another customer of this page; skipped.",
                customer.Id,
                tenantId
            );
            return null;
        }

        var emailOwner = await emails.FindActiveByAddressAsync(tenantId, emailResult.Value.NormalizedValue, ct);
        if (emailOwner is not null && emailOwner.CustomerId != customer.Id)
        {
            logger.LogWarning(
                "Customer {CustomerId} for tenant {TenantId} shares an email with existing customer {ExistingCustomerId}; skipped in backfill.",
                customer.Id,
                tenantId,
                emailOwner.CustomerId
            );
            return null;
        }

        await emails.AddAsync(CustomerEmailAddress.Create(tenantId, customer.Id, emailResult.Value), ct);
        return emailResult.Value.NormalizedValue;
    }
}
