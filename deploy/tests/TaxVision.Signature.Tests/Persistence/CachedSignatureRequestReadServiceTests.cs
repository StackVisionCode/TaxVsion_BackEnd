using Microsoft.Extensions.Caching.Distributed;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Queries.List;
using TaxVision.Signature.Infrastructure.Persistence.Queries;
using Xunit;

namespace TaxVision.Signature.Tests.Persistence;

/// <summary>
/// La clave de caché del listado DEBE distinguir `editableOnly`: Draft (editableOnly=true) y All
/// comparten Status=null, así que sin ese componente colisionaban y la pestaña Drafts devolvía
/// el listado completo cacheado (bug reportado 2026-09-22). Además, con visibilidad por asignación (P2)
/// el resultado depende del actor, así que la clave DEBE variar por usuario cuando no ve todo (sin fuga
/// de visibilidad entre empleados) y colapsar a una sola entrada para quien ve todo.
/// </summary>
public sealed class CachedSignatureRequestReadServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ListSignatureRequestsQuery Query(bool editableOnly, Guid? actor = null, bool canViewAll = true) =>
        new(
            Tenant,
            Status: null,
            Category: null,
            Page: 1,
            PageSize: 8,
            ActorUserId: actor ?? Guid.Empty,
            CanViewAll: canViewAll,
            EditableOnly: editableOnly
        );

    [Fact]
    public async Task Editable_y_no_editable_no_comparten_entrada_de_cache()
    {
        var inner = new CountingReadService();
        var cache = new InMemoryDistributedCache();
        var service = new CachedSignatureRequestReadService(inner, cache);

        var all = await service.ListAsync(Query(editableOnly: false));
        var drafts = await service.ListAsync(Query(editableOnly: true));

        // Dos claves distintas → dos fetches al inner (el de drafts NO se sirve del cache de All).
        Assert.Equal(2, inner.Calls);
        Assert.Equal(99, all.TotalCount); // rama "todas"
        Assert.Equal(1, drafts.TotalCount); // rama "solo borradores"
    }

    [Fact]
    public async Task La_misma_consulta_se_sirve_del_cache()
    {
        var inner = new CountingReadService();
        var service = new CachedSignatureRequestReadService(inner, new InMemoryDistributedCache());

        await service.ListAsync(Query(editableOnly: true));
        await service.ListAsync(Query(editableOnly: true));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task Dos_actores_distintos_sin_view_all_no_comparten_cache()
    {
        var inner = new CountingReadService();
        var service = new CachedSignatureRequestReadService(inner, new InMemoryDistributedCache());

        await service.ListAsync(Query(editableOnly: false, actor: Guid.NewGuid(), canViewAll: false));
        await service.ListAsync(Query(editableOnly: false, actor: Guid.NewGuid(), canViewAll: false));

        // Distinto actor (sin view_all) → distinta clave → dos fetches: nunca se sirve a un empleado
        // el listado cacheado de otro (fuga de visibilidad).
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task Actores_con_view_all_comparten_una_sola_entrada()
    {
        var inner = new CountingReadService();
        var service = new CachedSignatureRequestReadService(inner, new InMemoryDistributedCache());

        await service.ListAsync(Query(editableOnly: false, actor: Guid.NewGuid(), canViewAll: true));
        await service.ListAsync(Query(editableOnly: false, actor: Guid.NewGuid(), canViewAll: true));

        // view_all colapsa a u=Empty → misma clave → un solo fetch (todos ven lo mismo).
        Assert.Equal(1, inner.Calls);
    }

    private sealed class CountingReadService : ISignatureRequestReadService
    {
        public int Calls { get; private set; }

        public Task<ListSignatureRequestsResult> ListAsync(
            ListSignatureRequestsQuery query,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(
                new ListSignatureRequestsResult([], query.EditableOnly ? 1 : 99, query.Page, query.PageSize)
            );
        }
    }

    private sealed class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> store = new();

        public byte[]? Get(string key) => store.TryGetValue(key, out var value) ? value : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => store[key] = value;

        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default
        )
        {
            store[key] = value;
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => store.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            store.Remove(key);
            return Task.CompletedTask;
        }
    }
}
