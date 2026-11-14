using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Imports;

/// <summary>Reglas de acceso compartidas por los handlers, todas sobre proyecciones locales.</summary>
internal static class ImportAccessRules
{
    /// <summary>Oficina activa y plan con los módulos pedidos. Sin fila de oficina se considera activa.</summary>
    public static async Task<Result> EnsureTenantCanImportAsync(
        Guid tenantId,
        IFinancialAccessReader access,
        TenantEntitlementsResolver entitlements,
        IReadOnlyCollection<string> modules,
        CancellationToken ct
    )
    {
        if (await access.FindTenantAsync(tenantId, ct) is { IsActive: false })
            return Result.Failure(FinancialInsightsErrors.TenantInactive);

        var plan = await entitlements.GetOrPullAsync(tenantId, ct);
        if (plan is null)
            return Result.Failure(FinancialInsightsErrors.EntitlementsUnavailable);
        return modules.All(plan.HasModule) ? Result.Success() : Result.Failure(FinancialInsightsErrors.ModuleUnavailable);
    }

    /// <summary>
    /// El staff ve al cliente si existe en el directorio y, con la visibilidad encendida y sin
    /// customers.view_all, si lo tiene asignado. Para escribir además debe estar activo.
    /// </summary>
    public static async Task<CustomerDirectoryProjection?> FindVisibleCustomerAsync(
        Guid tenantId,
        Guid userId,
        Guid customerId,
        bool canViewAllCustomers,
        bool visibilityEnabled,
        bool requireActive,
        IFinancialAccessReader access,
        CancellationToken ct
    )
    {
        var customer = await access.FindCustomerAsync(tenantId, customerId, ct);
        if (customer is null || (requireActive && !customer.IsActive))
            return null;
        if (
            visibilityEnabled
            && !canViewAllCustomers
            && !await access.IsCustomerAssignedAsync(tenantId, customerId, userId, ct)
        )
            return null;
        return customer;
    }

    /// <summary>Consulta cada archivo; devuelve el primer error o el estado de todos.</summary>
    public static async Task<Result<Dictionary<Guid, ImportFileState>>> CheckFilesAsync(
        IReadOnlyCollection<Guid> fileIds,
        Guid customerId,
        bool allowPendingScan,
        IImportFileGateway files,
        CancellationToken ct
    )
    {
        var states = new Dictionary<Guid, ImportFileState>();
        foreach (var fileId in fileIds)
        {
            var state = await files.CheckAsync(fileId, customerId, ct);
            if (state == ImportFileState.Unavailable)
                return Result.Failure<Dictionary<Guid, ImportFileState>>(FinancialInsightsErrors.FileStatusUnavailable);
            if (state == ImportFileState.Ineligible || (state == ImportFileState.AwaitingScan && !allowPendingScan))
                return Result.Failure<Dictionary<Guid, ImportFileState>>(FinancialInsightsErrors.FileNotEligible(fileId));
            states[fileId] = state;
        }
        return Result.Success(states);
    }
}
