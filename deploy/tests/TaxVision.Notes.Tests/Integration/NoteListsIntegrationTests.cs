using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TaxVision.Notes.Api.Requests;
using TaxVision.Notes.Domain.Notes;
using Xunit;

namespace TaxVision.Notes.Tests.Integration;

/// <summary>
/// Fase 10 (03_Plan_De_Fases.md §Fase 10) — regresión de un bug real encontrado en la verificación
/// E2E en vivo: <c>NoteRepository.ListByReferenceAsync/ListForAuthorAsync/SearchAsync/ListClientVisibleAsync</c>
/// dependían del <c>HasQueryFilter</c> global fail-closed de <c>NotesDbContext</c> (poblado por
/// <c>ITenantContext</c>, alimentado por <c>JwtTenantContextMiddleware</c>), pero ese servicio scoped
/// no está garantizado poblado en el scope de DI que usa Wolverine para despachar localmente estas
/// queries — a diferencia de <c>GetByIdAsync</c>, que ya usaba <c>IgnoreQueryFilters()</c> con el
/// tenantId explícito (mismo patrón documentado en <c>feedback_ef_query_filter_wolverine_scope_mismatch.md</c>).
/// Sin el fix, estos 4 métodos siempre devolvían 0 filas en producción (contra WebApplicationFactory
/// SÍ pasaban porque los tests unitarios de handlers usan fakes en memoria, no el DbContext real —
/// por eso el bug sobrevivió hasta la verificación contra SQL Server real). Este test usa infra real
/// (SQL Server/Redis/RabbitMQ locales, WebApplicationFactory) para que un regreso futuro del bug
/// vuelva a fallar aquí.
/// </summary>
public sealed class NoteListsIntegrationTests : IClassFixture<NotesApiFactory>
{
    private readonly NotesApiFactory factory;

    public NoteListsIntegrationTests(NotesApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Created_note_is_visible_via_ListByReference_Mine_and_Search()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(tenantId, userId);

        var createResponse = await client.PostAsJsonAsync(
            "/notes",
            new CreateNoteRequest(
                "<p>Regresión de lista — verificación de query filter.</p>",
                NoteTargetType.Customer,
                targetId,
                NoteVisibility.Team,
                null
            )
        );
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var noteId = created.GetProperty("id").GetGuid();

        var byReference = await GetPagedItems(client, $"/notes?targetType=Customer&targetId={targetId}&page=1&size=20");
        Assert.Contains(byReference, item => item.GetProperty("id").GetGuid() == noteId);

        var mine = await GetPagedItems(client, "/notes/mine?page=1&size=20");
        Assert.Contains(mine, item => item.GetProperty("id").GetGuid() == noteId);

        var search = await GetPagedItems(client, "/notes/search?q=regresi%C3%B3n&page=1&size=20");
        Assert.Contains(search, item => item.GetProperty("id").GetGuid() == noteId);
    }

    private static async Task<List<JsonElement>> GetPagedItems(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray().ToList();
    }

    private HttpClient CreateAuthenticatedClient(Guid tenantId, Guid userId)
    {
        var client = factory.CreateClient();
        var token = JwtTestTokenFactory.Mint(factory, tenantId, userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
