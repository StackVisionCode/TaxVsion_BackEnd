using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Common;
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

namespace TaxVision.FinancialInsights.Infrastructure.Tenants;

public sealed class TenantDirectoryReconciliationOptions
{
    public const string SectionName = "FinancialInsights:TenantReconciliation";

    /// <summary>Encendida por defecto: nunca borra, solo agrega oficinas que faltan y refresca nombre y estado.</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 12;
    public int PageSize { get; set; } = 200;
}

/// <summary>Una oficina de <c>internal/tenants/directory</c>. Estado y versión vienen null si Tenant aún no los expone.</summary>
public sealed record RemoteTenant(Guid Id, string Name, string? Status, bool? IsActive, long? Version);

public interface IFinancialTenantDirectoryClient
{
    /// <summary>Una página del directorio (token de la PlatformTenant). Null ante cualquier fallo.</summary>
    Task<IReadOnlyList<RemoteTenant>?> ListPageAsync(int page, int size, CancellationToken ct = default);
}

public sealed class FinancialTenantDirectoryClient(
    HttpClient http,
    IServiceTokenAcquirer tokens,
    ILogger<FinancialTenantDirectoryClient> logger
) : IFinancialTenantDirectoryClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<RemoteTenant>?> ListPageAsync(int page, int size, CancellationToken ct = default)
    {
        var token = await tokens.GetTokenAsync(PlatformTenant.Id, ct);
        if (string.IsNullOrEmpty(token) || http.BaseAddress is null)
        {
            logger.LogWarning("Tenant directory call skipped: no service token or base URL.");
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"internal/tenants/directory?page={page}&size={size}"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Tenant directory call failed with status {Status}.", (int)response.StatusCode);
                return null;
            }
            var items = await response.Content.ReadFromJsonAsync<List<Item>>(Json, ct);
            return items
                ?.Select(i => new RemoteTenant(i.Id, i.Name ?? string.Empty, i.Status, i.IsActive, i.Version))
                .ToArray();
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested)
            )
        {
            logger.LogWarning("Tenant directory call failed with {ExceptionType}.", ex.GetType().Name);
            return null;
        }
    }

    private sealed record Item(Guid Id, string? Name, string? Status, bool? IsActive, long? Version);
}

public sealed record TenantDirectoryReconciliationOutcome(bool TenantReachable, int Inserted, int Refreshed);

/// <summary>
/// Siembra y repara <c>TenantDirectoryProjections</c> desde Tenant: cubre oficinas creadas antes de este servicio
/// (su TenantCreated nunca llegó) y eventos perdidos. Idempotente; guarda por página.
/// </summary>
public sealed class TenantDirectoryReconciler(
    IFinancialTenantDirectoryClient client,
    FinancialInsightsDbContext db,
    ILogger<TenantDirectoryReconciler> logger
)
{
    public async Task<TenantDirectoryReconciliationOutcome> RunAsync(int pageSize, CancellationToken ct)
    {
        // Foto actual de la fuente; la versión decide frente a un evento más nuevo.
        var observedAtUtc = DateTime.UtcNow;
        var (inserted, refreshed) = (0, 0);
        for (var page = 1; ; page++)
        {
            var items = await client.ListPageAsync(page, pageSize, ct);
            if (items is null)
            {
                logger.LogWarning("Tenant directory reconciliation aborted on page {Page}.", page);
                return new(false, inserted, refreshed);
            }

            var ids = items.Select(i => i.Id).ToArray();
            var rows = await db
                .TenantDirectoryProjections.IgnoreQueryFilters()
                .Where(r => ids.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);
            foreach (var tenant in items)
            {
                if (!rows.TryGetValue(tenant.Id, out var row))
                {
                    row = TenantDirectoryProjection.Create(tenant.Id, tenant.Name, observedAtUtc);
                    db.TenantDirectoryProjections.Add(row);
                    inserted++;
                }
                else
                    refreshed++;

                if (tenant.Status is not null && tenant.IsActive is not null)
                    row.ApplyStatus(tenant.Status, tenant.IsActive.Value, tenant.Version, observedAtUtc);
                row.ApplyName(tenant.Name, tenant.Version, observedAtUtc);
            }
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            if (items.Count < pageSize)
                break;
        }

        if (inserted > 0)
            logger.LogInformation(
                "Tenant directory reconciled: {Inserted} inserted, {Refreshed} refreshed.",
                inserted,
                refreshed
            );
        return new(true, inserted, refreshed);
    }
}

public sealed class TenantDirectoryReconciliationJob(
    IServiceProvider services,
    ILogger<TenantDirectoryReconciliationJob> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await services.GetRequiredService<IHostApplicationLifetime>().WaitForApplicationStartedAsync(stoppingToken);
        var options = services.GetRequiredService<IOptions<TenantDirectoryReconciliationOptions>>().Value;
        if (!options.Enabled)
            return;

        var interval = TimeSpan.FromHours(Math.Max(1, options.IntervalHours));
        while (!stoppingToken.IsCancellationRequested)
        {
            var reachable = false;
            try
            {
                using var scope = services.CreateScope();
                var correlation = scope.ServiceProvider.GetRequiredService<ICorrelationContext>();
                using (correlation.Push(Guid.NewGuid().ToString("N")))
                    reachable = (
                        await scope
                            .ServiceProvider.GetRequiredService<TenantDirectoryReconciler>()
                            .RunAsync(Math.Clamp(options.PageSize, 1, 200), stoppingToken)
                    ).TenantReachable;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "TenantDirectoryReconciliationJob iteration failed.");
            }
            try
            {
                // Si Tenant aún no arranca, reintenta pronto en vez de esperar el ciclo completo.
                await Task.Delay(reachable ? interval : TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }
}
