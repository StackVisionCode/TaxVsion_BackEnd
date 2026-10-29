using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Sms.Api.Controllers;
using TaxVision.Sms.Application.Messages.Queries;
using TaxVision.Sms.Application.OptOut.Queries;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using static TaxVision.Sms.Api.Controllers.SmsReadController;

namespace TaxVision.Sms.Tests.Api;

/// <summary>
/// POST /sms/{messages,conversations,optouts}/search: el término (nombre, teléfono o email) viaja en el
/// cuerpo y no en la URL; la búsqueda es la misma que el GET — mismo query, mismos gates, misma respuesta.
/// </summary>
public sealed class SmsReadControllerSearchTests
{
    private const string CrmSourceContext = "crm-sms";
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    public static TheoryData<string, string> SearchPairs =>
        new()
        {
            { nameof(SmsReadController.ListMessages), nameof(SmsReadController.SearchMessages) },
            { nameof(SmsReadController.ListConversations), nameof(SmsReadController.SearchConversations) },
            { nameof(SmsReadController.ListOptOuts), nameof(SmsReadController.SearchOptOuts) },
        };

    [Fact]
    public async Task SearchMessages_manda_el_termino_y_los_filtros_del_cuerpo_al_mismo_query()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: true);
        var customerId = Guid.NewGuid();
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(7);

        var response = await controller.SearchMessages(
            new SearchMessagesRequest(customerId, SmsMessageStatusFilter.Failed, "ana@example.com", from, to, 3, 25),
            CancellationToken.None
        );

        Assert.Equal(
            new SearchSmsMessagesQuery(
                TenantId,
                customerId,
                SmsMessageStatusFilter.Failed,
                "ana@example.com",
                from,
                to,
                CrmSourceContext,
                3,
                25,
                UserId,
                CanViewAll: true
            ),
            Assert.Single(bus.Invoked)
        );
        Assert.Same(bus.Messages, Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task SearchConversations_manda_el_termino_del_cuerpo_al_mismo_query()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: false);

        var response = await controller.SearchConversations(
            new SearchConversationsRequest("+15551234567", 2, 10),
            CancellationToken.None
        );

        Assert.Equal(
            new SearchSmsConversationsQuery(
                TenantId,
                "+15551234567",
                CrmSourceContext,
                2,
                10,
                UserId,
                CanViewAll: false
            ),
            Assert.Single(bus.Invoked)
        );
        Assert.Same(bus.Conversations, Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task SearchOptOuts_manda_el_termino_y_el_estado_del_cuerpo_al_mismo_query()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: true);

        var response = await controller.SearchOptOuts(
            new SearchOptOutsRequest(SmsOptOutStatusFilter.OptedOut, "Ana", 4, 50),
            CancellationToken.None
        );

        Assert.Equal(
            new SearchSmsOptOutsQuery(TenantId, SmsOptOutStatusFilter.OptedOut, "Ana", 4, 50, UserId, CanViewAll: true),
            Assert.Single(bus.Invoked)
        );
        Assert.Same(bus.OptOuts, Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task POST_y_GET_construyen_el_mismo_query_para_los_mismos_filtros()
    {
        var customerId = Guid.NewGuid();
        var from = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        var post = new RecordingBus();
        var postController = BuildController(post, canViewAll: false);
        await postController.SearchMessages(
            new SearchMessagesRequest(customerId, SmsMessageStatusFilter.Delivered, "ana", from, null, 2, 10),
            CancellationToken.None
        );
        await postController.SearchConversations(new SearchConversationsRequest("ana", 2, 10), CancellationToken.None);
        await postController.SearchOptOuts(
            new SearchOptOutsRequest(SmsOptOutStatusFilter.Subscribed, "ana", 2, 10),
            CancellationToken.None
        );

        var get = new RecordingBus();
        var getController = BuildController(get, canViewAll: false);
        await getController.ListMessages(customerId, SmsMessageStatusFilter.Delivered, "ana", from, null, 2, 10);
        await getController.ListConversations("ana", 2, 10);
        await getController.ListOptOuts(SmsOptOutStatusFilter.Subscribed, "ana", 2, 10);

        Assert.Equal(get.Invoked, post.Invoked);
        // El GET de listado no cambia sus cabeceras (no se marca no-store).
        Assert.False(getController.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task POST_sin_tenant_en_el_token_responde_401_sin_consultar()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: true, withIdentity: false);

        Assert.IsType<UnauthorizedResult>(
            await controller.SearchMessages(new SearchMessagesRequest(Term: "ana"), CancellationToken.None)
        );
        Assert.IsType<UnauthorizedResult>(
            await controller.SearchConversations(new SearchConversationsRequest("ana"), CancellationToken.None)
        );
        Assert.IsType<UnauthorizedResult>(
            await controller.SearchOptOuts(new SearchOptOutsRequest(Term: "ana"), CancellationToken.None)
        );
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public void Los_cuerpos_sin_filtros_toman_los_mismos_defaults_que_el_GET()
    {
        // Mismas opciones JSON que Program.cs (camelCase web + enums como string).
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        const string json = """{"term":"ana@example.com"}""";

        Assert.Equal(
            new SearchMessagesRequest(null, SmsMessageStatusFilter.All, "ana@example.com", null, null, 1, 20),
            JsonSerializer.Deserialize<SearchMessagesRequest>(json, options)
        );
        Assert.Equal(
            new SearchConversationsRequest("ana@example.com", 1, 20),
            JsonSerializer.Deserialize<SearchConversationsRequest>(json, options)
        );
        Assert.Equal(
            new SearchOptOutsRequest(SmsOptOutStatusFilter.All, "ana@example.com", 1, 20),
            JsonSerializer.Deserialize<SearchOptOutsRequest>(json, options)
        );
    }

    [Fact]
    public void El_cuerpo_de_mensajes_usa_los_mismos_nombres_from_to_que_el_GET()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var body = JsonSerializer.Deserialize<SearchMessagesRequest>(
            """{"status":"Failed","from":"2026-01-01T00:00:00Z","to":"2026-01-08T00:00:00Z"}""",
            options
        )!;

        Assert.Equal(SmsMessageStatusFilter.Failed, body.Status);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), body.From);
        Assert.Equal(new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc), body.To);
    }

    [Theory]
    [MemberData(nameof(SearchPairs))]
    public void El_POST_search_tiene_los_mismos_gates_que_su_GET(string getName, string postName)
    {
        var get = typeof(SmsReadController).GetMethod(getName)!;
        var post = typeof(SmsReadController).GetMethod(postName)!;
        var getTemplate = get.GetCustomAttribute<HttpGetAttribute>()!.Template;

        Assert.Equal($"{getTemplate}/search", post.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            get.GetCustomAttribute<RateLimitAttribute>()!.PolicyName,
            post.GetCustomAttribute<RateLimitAttribute>()!.PolicyName
        );
        Assert.Equal(get.ReturnType, post.ReturnType);
        Assert.Equal(ProducesOk(get), ProducesOk(post));
        // Permiso y actor types salen del controller: ninguno de los dos los redefine por acción.
        Assert.Null(post.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Null(post.GetCustomAttribute<AllowActorTypesAttribute>());
        Assert.Null(get.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Null(get.GetCustomAttribute<AllowActorTypesAttribute>());
    }

    [Fact]
    public void El_controller_exige_sms_read_y_los_actor_types_del_read_model()
    {
        var type = typeof(SmsReadController);

        Assert.Equal(
            HasPermissionAttribute.PolicyPrefix + BuildingBlocks.Authorization.SmsPermissions.Read,
            type.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.Equal(
            new[] { ActorType.Service, ActorType.TenantAdmin, ActorType.TenantEmployee }.OrderBy(a => a),
            type.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes.OrderBy(a => a)
        );
    }

    private static Type? ProducesOk(MethodInfo method) =>
        method
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(a => a.StatusCode == StatusCodes.Status200OK)
            .Type;

    private static SmsReadController BuildController(RecordingBus bus, bool canViewAll, bool withIdentity = true)
    {
        var claims = withIdentity
            ? new List<Claim> { new(ClaimNames.TenantId, TenantId.ToString()), new("sub", UserId.ToString()) }
            : [];

        return new SmsReadController(bus, new FixedPermissions(canViewAll))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) },
            },
        };
    }

    private sealed class FixedPermissions(bool granted) : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(granted);
    }

    /// <summary>Captura lo invocado y responde una página vacía del tipo pedido.</summary>
    private sealed class RecordingBus : IMessageBus
    {
        public List<object> Invoked { get; } = [];

        public PagedResult<SmsMessageSummaryResponse> Messages { get; } = new([], 1, 20, 0);

        public PagedResult<SmsConversationSummaryResponse> Conversations { get; } = new([], 1, 20, 0);

        public PagedResult<SmsOptOutSummaryResponse> OptOuts { get; } = new([], 1, 20, 0);

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Invoked.Add(message);
            object result = message switch
            {
                SearchSmsMessagesQuery => Messages,
                SearchSmsConversationsQuery => Conversations,
                SearchSmsOptOutsQuery => OptOuts,
                _ => throw new NotSupportedException(message.GetType().Name),
            };
            return Task.FromResult((T)result);
        }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) => ValueTask.CompletedTask;

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public string? TenantId
        {
            get => null;
            set { }
        }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotImplementedException();
    }
}
