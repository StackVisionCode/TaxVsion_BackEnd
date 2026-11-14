using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Infrastructure.Customers;

/// <summary>Reconciliación del directorio local de clientes contra Customer (mismo esquema que Notes/Tasks).</summary>
public sealed class CustomerDirectoryReconciliationOptions
{
    public const string SectionName = "FinancialInsights:CustomerReconciliation";

    /// <summary>Encendida por defecto: nunca borra, solo agrega filas que faltan y refresca nombre y estado.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 12;
    public int PageSize { get; set; } = 200;
    public int NamesIntervalHours { get; set; } = 6;
    public int TenantLimitPerRun { get; set; } = 100;
}

/// <summary>
/// Siembra y repara el directorio completo (todos los tenants) desde <c>internal/customers/reconciliation</c>: cubre
/// clientes creados antes de este servicio y eventos perdidos. Idempotente; guarda por página.
/// </summary>
public sealed class TenantCustomerFullReconciliationJob(
    IServiceProvider services,
    ILogger<TenantCustomerFullReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await services.GetRequiredService<IHostApplicationLifetime>().WaitForApplicationStartedAsync(stoppingToken);
        var options = services.GetRequiredService<IOptions<CustomerDirectoryReconciliationOptions>>().Value;
        if (!options.Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(Math.Max(1, options.PageSize), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "TenantCustomerFullReconciliationJob iteration failed.");
            }
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(int pageSize, CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IFinancialCustomerDirectoryClient>();
        var db = scope.ServiceProvider.GetRequiredService<FinancialInsightsDbContext>();
        var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
        using (correlation.Push(Guid.NewGuid().ToString("N")))
        {
            // Foto actual de la fuente: siempre más nueva que cualquier evento pasado.
            var observedAtUtc = DateTime.UtcNow;
            var (page, inserted, refreshed) = (1, 0, 0);
            while (true)
            {
                var result = await client.ListAllAsync(page, pageSize, ct);
                if (result is null)
                {
                    logger.LogWarning("TenantCustomerFullReconciliationJob aborted on page {Page}.", page);
                    return;
                }
                foreach (var customer in result.Items)
                {
                    if (await CustomerDirectoryUpsert.ApplyAsync(db, customer, observedAtUtc, ct))
                        inserted++;
                    else
                        refreshed++;
                }
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
                if (!result.HasMore)
                    break;
                page++;
            }
            if (inserted > 0)
                logger.LogInformation(
                    "Customer directory reconciled: {Inserted} inserted, {Refreshed} refreshed.",
                    inserted,
                    refreshed
                );
        }
    }
}

/// <summary>Completa el nombre de los clientes que llegaron sin él (alta masiva) re-paginando su tenant.</summary>
public sealed class CustomerDirectoryReconciliationJob(
    IServiceProvider services,
    ILogger<CustomerDirectoryReconciliationJob> logger
) : BackgroundService
{
    private const int PageSize = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await services.GetRequiredService<IHostApplicationLifetime>().WaitForApplicationStartedAsync(stoppingToken);
        var options = services.GetRequiredService<IOptions<CustomerDirectoryReconciliationOptions>>().Value;
        if (!options.Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, options.NamesIntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(Math.Max(1, options.TenantLimitPerRun), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "CustomerDirectoryReconciliationJob iteration failed.");
            }
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(int tenantLimit, CancellationToken ct)
    {
        List<Guid> tenants;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinancialInsightsDbContext>();
            tenants = await db
                .CustomerDirectoryProjections.IgnoreQueryFilters()
                .Where(c => c.DisplayName == null)
                .Select(c => c.TenantId)
                .Distinct()
                .Take(tenantLimit)
                .ToListAsync(ct);
        }

        foreach (var tenantId in tenants)
        {
            // Un tenant que falla no frena a los demás; cada uno en su scope.
            using var scope = services.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IFinancialCustomerDirectoryClient>();
            var db = scope.ServiceProvider.GetRequiredService<FinancialInsightsDbContext>();
            try
            {
                var observedAtUtc = DateTime.UtcNow;
                for (var page = 1; ; page++)
                {
                    var result = await client.ListActiveAsync(tenantId, page, PageSize, ct);
                    if (result is null)
                        break;
                    var missing = await db
                        .CustomerDirectoryProjections.IgnoreQueryFilters()
                        .Where(c => c.TenantId == tenantId && c.DisplayName == null)
                        .ToDictionaryAsync(c => c.CustomerId, ct);
                    foreach (var customer in result.Items)
                    {
                        if (missing.TryGetValue(customer.CustomerId, out var row) && customer.DisplayName is not null)
                            row.Apply(customer.DisplayName, row.IsActive, observedAtUtc);
                    }
                    await db.SaveChangesAsync(ct);
                    if (!result.HasMore)
                        break;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Customer names reconciliation failed for tenant {TenantId}.", tenantId);
            }
        }
    }
}

internal static class CustomerDirectoryUpsert
{
    /// <summary>Inserta o refresca una fila. <c>true</c> si la insertó.</summary>
    public static async Task<bool> ApplyAsync(
        FinancialInsightsDbContext db,
        RemoteCustomer customer,
        DateTime observedAtUtc,
        CancellationToken ct
    )
    {
        var row = await db
            .CustomerDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(c => c.TenantId == customer.TenantId && c.CustomerId == customer.CustomerId, ct);
        if (row is not null)
        {
            row.Apply(customer.DisplayName, customer.IsActive, observedAtUtc);
            return false;
        }
        row = CustomerDirectoryProjection.Create(customer.TenantId, customer.CustomerId, customer.DisplayName, observedAtUtc);
        row.Apply(customer.DisplayName, customer.IsActive, observedAtUtc);
        db.CustomerDirectoryProjections.Add(row);
        return true;
    }
}

/// <summary>Token de la PlatformTenant para la reconciliación de asignaciones del kit de visibilidad.</summary>
internal sealed class FinancialPlatformTokenProvider(IServiceTokenAcquirer tokens) : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) => tokens.GetTokenAsync(PlatformTenant.Id, ct);
}
