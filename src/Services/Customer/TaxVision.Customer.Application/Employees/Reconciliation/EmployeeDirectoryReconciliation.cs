using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Employees;

namespace TaxVision.Customer.Application.Employees.Reconciliation;

public sealed record EmployeeDirectoryReconciliationResult(int Created, int Updated);

/// <summary>
/// Repara el directorio de empleados con una página del staff de Auth: crea a quien nunca llegó por evento
/// (usuarios anteriores a la proyección) y corrige tipo y actividad. Sin efectos colaterales: no publica
/// eventos ni toca asignaciones de clientes.
/// </summary>
public static class EmployeeDirectoryReconciliation
{
    public static async Task<EmployeeDirectoryReconciliationResult> ApplyPageAsync(
        IReadOnlyList<RemoteStaffUser> users,
        ITenantEmployeeDirectoryReconciliationRepository directory,
        ILogger logger,
        CancellationToken ct
    )
    {
        var existing = await directory.GetByUserIdsAsync(users.Select(user => user.UserId).ToList(), ct);
        var created = 0;
        var updated = 0;
        foreach (var user in users)
        {
            if (!existing.TryGetValue(user.UserId, out var entry))
            {
                directory.Add(
                    TenantEmployeeDirectoryEntry.CreateReconciled(
                        user.UserId,
                        user.TenantId,
                        user.ActorType,
                        user.IsActive,
                        user.IsOffboarded
                    )
                );
                created++;
                continue;
            }
            if (Reconcile(entry, user, logger))
                updated++;
        }
        return new EmployeeDirectoryReconciliationResult(created, updated);
    }

    private static bool Reconcile(TenantEmployeeDirectoryEntry entry, RemoteStaffUser user, ILogger logger)
    {
        if (entry.TenantId != user.TenantId)
        {
            logger.LogWarning(
                "Employee directory entry {UserId} belongs to another tenant than Auth reports; left unchanged.",
                user.UserId
            );
            return false;
        }
        var changed = entry.ReconcileWith(user.ActorType, user.IsActive);
        if (user.IsOffboarded && !entry.IsOffboarded)
            logger.LogWarning(
                "User {UserId} is offboarded in Auth but not in the employee directory; only marked inactive.",
                user.UserId
            );
        return changed;
    }
}
