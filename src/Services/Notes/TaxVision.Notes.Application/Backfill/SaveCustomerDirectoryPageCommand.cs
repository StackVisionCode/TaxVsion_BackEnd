using BuildingBlocks.Persistence;
using TaxVision.Notes.Application.Backfill.Abstractions;
using TaxVision.Notes.Application.Customers.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Backfill;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Backfill;

public sealed record SaveCustomerDirectoryPageCommand(
    Guid TenantId,
    IReadOnlyList<RemoteCustomerSummary> Customers,
    DateTime ReadAtUtc,
    bool IsLastPage
);

/// <summary>Una página, una transacción. La última marca el tenant como sembrado, en la misma.</summary>
public static class SaveCustomerDirectoryPageHandler
{
    public static async Task Handle(
        SaveCustomerDirectoryPageCommand command,
        ICustomerDirectoryRepository directory,
        ITenantBackfillStateRepository states,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        foreach (var customer in command.Customers)
            await UpsertAsync(command, customer, directory, ct);

        if (command.IsLastPage && await states.GetByTenantIdAsync(command.TenantId, ct) is null)
            await states.AddAsync(TenantBackfillState.Create(command.TenantId), ct);

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task UpsertAsync(
        SaveCustomerDirectoryPageCommand command,
        RemoteCustomerSummary customer,
        ICustomerDirectoryRepository directory,
        CancellationToken ct
    )
    {
        var status = customer.IsActive ? CustomerDirectoryStatus.Active : CustomerDirectoryStatus.Inactive;
        var existing = await directory.GetByCustomerIdAsync(command.TenantId, customer.Id, ct);
        if (existing is not null)
        {
            existing.ApplyIfNewer(customer.DisplayName, status, command.ReadAtUtc);
            return;
        }

        await directory.AddAsync(
            CustomerDirectoryEntry.Create(
                command.TenantId,
                customer.Id,
                customer.DisplayName,
                status,
                command.ReadAtUtc
            ),
            ct
        );
    }
}
