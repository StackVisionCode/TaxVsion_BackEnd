using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Contracts.Files;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Library;

namespace TaxVision.CloudStorage.Infrastructure.Library;

/// <summary>
/// <c>POST {dueño}/files/visibility</c> y <c>GET communication/files/scope</c> con el token de la petición entrante:
/// el dueño aplica su regla con la identidad real de quien mira. Falla cerrado: sin URL, sin token, tiempo agotado o
/// cualquier respuesta que no sea 200 o 403 cuenta como "no disponible". El token nunca se escribe en logs.
/// </summary>
public sealed class HttpFileOwnerVisibilityClient(
    IHttpClientFactory clients,
    IHttpContextAccessor context,
    IOptions<LibraryOwnerOptions> options,
    ILogger<HttpFileOwnerVisibilityClient> logger
) : IFileOwnerVisibilityClient
{
    public const string TasksClient = "library-owner-tasks";
    public const string CorrespondenceClient = "library-owner-correspondence";
    public const string CommunicationClient = "library-owner-communication";
    public const string NotesClient = "library-owner-notes";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ClientNameOf(FileOwnerService owner) =>
        owner switch
        {
            FileOwnerService.Tasks => TasksClient,
            FileOwnerService.Correspondence => CorrespondenceClient,
            FileOwnerService.Communication => CommunicationClient,
            _ => NotesClient,
        };

    public async Task<OwnerVisibility> CheckAsync(
        FileOwnerService owner,
        IReadOnlyCollection<Guid> fileIds,
        string purpose,
        CancellationToken ct
    )
    {
        if (fileIds.Count == 0)
            return new OwnerVisibility(true, new Dictionary<Guid, VisibleFile>());
        var bearer = IncomingBearer();
        var http = clients.CreateClient(ClientNameOf(owner));
        if (bearer is null || http.BaseAddress is null)
        {
            logger.LogWarning("Library owner {Owner} skipped: no base URL or no incoming token.", owner);
            return OwnerVisibility.Unavailable;
        }

        var visible = new Dictionary<Guid, VisibleFile>();
        foreach (var batch in fileIds.Distinct().Chunk(FileVisibilityLimits.Batch))
        {
            if (await AskAsync(http, owner, bearer, batch, purpose, ct) is not { } items)
                return OwnerVisibility.Unavailable;
            // Solo lo que se preguntó.
            foreach (var item in items.Where(item => batch.Contains(item.FileId)))
                visible[item.FileId] = item;
        }
        return new OwnerVisibility(true, visible);
    }

    public async Task<OwnerScope> GetCommunicationScopeAsync(CancellationToken ct)
    {
        var bearer = IncomingBearer();
        var http = clients.CreateClient(CommunicationClient);
        if (bearer is null || http.BaseAddress is null)
        {
            logger.LogWarning("Library scope skipped: no base URL or no incoming token.");
            return OwnerScope.Unavailable;
        }

        // Tope propio, más corto que el de la visibilidad.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.ScopeTimeoutSeconds)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "communication/files/scope");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var scope = await SendAsync(http, FileOwnerService.Communication, request, OwnerScope.Empty, timeout.Token, ct);
        return scope is null ? OwnerScope.Unavailable : new OwnerScope(true, scope);
    }

    private async Task<IReadOnlyList<VisibleFile>?> AskAsync(
        HttpClient http,
        FileOwnerService owner,
        string bearer,
        Guid[] fileIds,
        string purpose,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, PathOf(owner))
        {
            Content = JsonContent.Create(new FileVisibilityRequest(fileIds, purpose), options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var body = await SendAsync(http, owner, request, new FileVisibilityResponse([]), ct, ct);
        return body?.Items;
    }

    // null = no disponible; 403 = el dueño niega el permiso a esa persona: no ve nada, no es avería. Un corte que no
    // viene de la petición entrante es un tiempo agotado.
    private async Task<T?> SendAsync<T>(
        HttpClient http,
        FileOwnerService owner,
        HttpRequestMessage request,
        T forbidden,
        CancellationToken token,
        CancellationToken requestCt
    )
        where T : class
    {
        try
        {
            using var response = await http.SendAsync(request, token);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return forbidden;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Library owner {Owner} answered {Status}.", owner, (int)response.StatusCode);
                return null;
            }
            return await response.Content.ReadFromJsonAsync<T>(Json, token);
        }
        catch (Exception ex)
            when (ex is HttpRequestException or JsonException or NotSupportedException
                || (ex is OperationCanceledException && !requestCt.IsCancellationRequested)
            )
        {
            logger.LogWarning("Library owner {Owner} did not answer ({Error}).", owner, ex.GetType().Name);
            return null;
        }
    }

    private static string PathOf(FileOwnerService owner) =>
        owner switch
        {
            FileOwnerService.Tasks => "tasks/files/visibility",
            FileOwnerService.Correspondence => "correspondence/files/visibility",
            FileOwnerService.Communication => "communication/files/visibility",
            _ => "notes/files/visibility",
        };

    private string? IncomingBearer()
    {
        var header = context.HttpContext?.Request.Headers.Authorization.ToString();
        return header is { Length: > 7 } && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..].Trim()
            : null;
    }
}
