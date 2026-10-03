using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;
using TaxVision.Postmaster.Infrastructure.Providers.TenantDirectory;
using TaxVision.Postmaster.Infrastructure.Seed;

namespace TaxVision.Postmaster.Tests.Seed;

/// <summary>
/// El backfill que rellena el directorio con las oficinas que ya existían cuando se desplegó la
/// tabla. Sin él, <c>TenantCreatedIntegrationEvent</c> solo cubriría las nuevas y todos los clientes
/// de hoy seguirían viendo <c>From: TaxVision</c> para siempre.
///
/// <para>Lo que se prueba es la lógica de paginado y de idempotencia, que es donde están los dos
/// fallos que importan: parar antes de tiempo (quedan oficinas sin nombre y nadie se entera) o
/// reescribir en cada arranque (escrituras y una versión nueva de cada fila por cada reinicio, que
/// con varias réplicas es un goteo permanente).</para>
///
/// <para>Se ejerce <see cref="BackfillPlan"/>, la parte pura; el hosted service solo la envuelve en
/// un scope de DI. Arrancarlo de verdad traería el host entero contra la base real sin probar nada
/// más que eso.</para>
/// </summary>
public sealed class TenantDirectoryBackfillTests
{
    [Fact]
    public async Task It_writes_only_the_tenants_that_are_missing()
    {
        var known = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var repository = new FakeDirectory();
        await repository.UpsertAsync(known, "Ya proyectada", "ya", DateTime.UtcNow);
        var client = new PagedClient([new(known, "Ya proyectada", "ya"), new(missing, "Nueva", "nueva")]);

        var added = await BackfillPlan.RunAsync(client, repository, pageSize: 100, CancellationToken.None);

        Assert.Equal(1, added);
        Assert.Equal(2, repository.Names.Count);
        Assert.Equal("Nueva", repository.Names[missing]);
    }

    [Fact]
    public async Task A_second_run_writes_nothing()
    {
        // La propiedad que hace seguro dejarlo en cada arranque. Si fallara, cada reinicio (despliegue,
        // escalado, reinicio del contenedor) reescribiría todas las filas.
        var repository = new FakeDirectory();
        var client = new PagedClient([new(Guid.NewGuid(), "Una", "una"), new(Guid.NewGuid(), "Otra", "otra")]);

        Assert.Equal(2, await BackfillPlan.RunAsync(client, repository, 100, CancellationToken.None));
        Assert.Equal(0, await BackfillPlan.RunAsync(client, repository, 100, CancellationToken.None));
    }

    [Fact]
    public async Task It_keeps_paging_until_the_last_page()
    {
        // Con 5 oficinas y páginas de 2, parar en la primera dejaría 3 sin nombre. El síntoma sería
        // "a unas oficinas les sale el nombre y a otras no", que nadie atribuye a un backfill.
        var tenants = Enumerable
            .Range(0, 5)
            .Select(i => new TenantDirectoryItem(Guid.NewGuid(), $"Of{i}", $"of{i}"))
            .ToList();
        var repository = new FakeDirectory();

        var added = await BackfillPlan.RunAsync(
            new PagedClient(tenants),
            repository,
            pageSize: 2,
            CancellationToken.None
        );

        Assert.Equal(5, added);
        Assert.Equal(5, repository.Names.Count);
    }

    [Fact]
    public async Task An_exact_multiple_of_the_page_size_still_terminates()
    {
        // 4 oficinas en páginas de 2: la última página viene llena, así que hace falta pedir una más
        // para saber que se acabó. Un corte mal puesto acá o deja fuera la última página o no para.
        var tenants = Enumerable
            .Range(0, 4)
            .Select(i => new TenantDirectoryItem(Guid.NewGuid(), $"Of{i}", $"of{i}"))
            .ToList();
        var client = new PagedClient(tenants);
        var repository = new FakeDirectory();

        Assert.Equal(4, await BackfillPlan.RunAsync(client, repository, pageSize: 2, CancellationToken.None));
        Assert.Equal(3, client.PagesRequested);
    }

    [Fact]
    public async Task A_tenant_service_that_does_not_answer_leaves_the_directory_untouched()
    {
        // Que Tenant esté caído al arrancar no puede impedir que Postmaster arranque ni corromper lo
        // que ya hay: se reintenta en el siguiente boot.
        var repository = new FakeDirectory();

        var added = await BackfillPlan.RunAsync(new SilentClient(), repository, 100, CancellationToken.None);

        Assert.Equal(0, added);
        Assert.Empty(repository.Names);
    }

    private sealed class PagedClient(IReadOnlyList<TenantDirectoryItem> all) : ITenantDirectoryClient
    {
        public int PagesRequested { get; private set; }

        public Task<IReadOnlyList<TenantDirectoryItem>> GetPageAsync(int page, int size, CancellationToken ct = default)
        {
            PagesRequested++;
            return Task.FromResult<IReadOnlyList<TenantDirectoryItem>>(all.Skip((page - 1) * size).Take(size).ToList());
        }
    }

    private sealed class SilentClient : ITenantDirectoryClient
    {
        public Task<IReadOnlyList<TenantDirectoryItem>> GetPageAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<TenantDirectoryItem>>([]);
    }

    private sealed class FakeDirectory : ITenantDirectoryRepository
    {
        public Dictionary<Guid, string> Names { get; } = [];

        public Task<TenantDirectoryEntry?> FindAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(
                Names.TryGetValue(tenantId, out var name)
                    ? TenantDirectoryEntry.Create(tenantId, name, "x", DateTime.UtcNow)
                    : null
            );

        public Task UpsertAsync(
            Guid tenantId,
            string name,
            string subDomain,
            DateTime nowUtc,
            CancellationToken ct = default
        )
        {
            Names[tenantId] = name;
            return Task.CompletedTask;
        }

        public Task<IReadOnlySet<Guid>> GetKnownTenantIdsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<Guid>>(Names.Keys.ToHashSet());
    }
}
