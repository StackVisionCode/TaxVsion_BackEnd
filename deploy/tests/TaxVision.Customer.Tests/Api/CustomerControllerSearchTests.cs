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
using TaxVision.Customer.Api.Controllers;
using TaxVision.Customer.Api.Requests;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Queries.Search;
using TaxVision.Customer.Domain.Customers;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Customer.Tests.Api;

/// <summary>
/// POST /customers/search: el término viaja en el cuerpo (no en la URL que acaba en los logs) y la
/// búsqueda es la misma que GET /customers — mismo query, mismos gates, misma forma de respuesta.
/// </summary>
public sealed class CustomerControllerSearchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task SearchByBody_manda_el_termino_y_los_filtros_del_cuerpo_al_mismo_query()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: true);

        var response = await controller.SearchByBody(
            new SearchCustomersRequest(
                "ana@example.com",
                CustomerStatusFilter.NotArchived,
                3,
                25,
                CustomerKind.Business,
                CustomerSortField.Created,
                CustomerSortDirection.Desc
            ),
            CancellationToken.None
        );

        var query = Assert.IsType<SearchCustomersQuery>(Assert.Single(bus.Invoked));
        Assert.Equal(
            new SearchCustomersQuery(
                TenantId,
                "ana@example.com",
                CustomerStatusFilter.NotArchived,
                3,
                25,
                UserId,
                CanViewAll: true,
                CustomerKind.Business,
                CustomerSortField.Created,
                CustomerSortDirection.Desc
            ),
            query
        );
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        Assert.Same(bus.Result, ok.Value);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.False(controller.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task SearchByBody_y_Search_construyen_el_mismo_query_para_los_mismos_filtros()
    {
        var postBus = new RecordingBus();
        await BuildController(postBus, canViewAll: false)
            .SearchByBody(new SearchCustomersRequest("ana", CustomerStatusFilter.All, 2, 10), CancellationToken.None);

        var getBus = new RecordingBus();
        await BuildController(getBus, canViewAll: false)
            .Search("ana", CustomerStatusFilter.All, 2, 10, ct: CancellationToken.None);

        Assert.Equal(Assert.Single(getBus.Invoked), Assert.Single(postBus.Invoked));
        Assert.False(((SearchCustomersQuery)postBus.Invoked[0]).CanViewAll);
    }

    [Fact]
    public async Task SearchByBody_sin_tenant_en_el_token_responde_401_sin_consultar()
    {
        var bus = new RecordingBus();
        var controller = BuildController(bus, canViewAll: true, withIdentity: false);

        var response = await controller.SearchByBody(new SearchCustomersRequest("ana"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(response.Result);
        Assert.Empty(bus.Invoked);
    }

    [Fact]
    public void El_cuerpo_sin_filtros_toma_los_mismos_defaults_que_el_GET()
    {
        // Mismas opciones JSON que Program.cs (camelCase web + enums como string).
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        var body = JsonSerializer.Deserialize<SearchCustomersRequest>("""{"term":"ana@example.com"}""", options)!;

        Assert.Equal(new SearchCustomersRequest("ana@example.com"), body);
        Assert.Equal(CustomerStatusFilter.Active, body.Status);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.Size);
        Assert.Null(body.Kind);
        Assert.Equal(CustomerSortField.Name, body.Sort);
        Assert.Equal(CustomerSortDirection.Asc, body.Dir);
    }

    [Fact]
    public void SearchByBody_es_POST_search_con_los_mismos_gates_que_el_GET()
    {
        var get = typeof(CustomerController).GetMethod(nameof(CustomerController.Search))!;
        var post = typeof(CustomerController).GetMethod(nameof(CustomerController.SearchByBody))!;

        Assert.Equal("search", post.GetCustomAttribute<HttpPostAttribute>()?.Template);
        Assert.Equal(
            get.GetCustomAttribute<HasPermissionAttribute>()!.Policy,
            post.GetCustomAttribute<HasPermissionAttribute>()!.Policy
        );
        Assert.Equal(
            get.GetCustomAttribute<RateLimitAttribute>()!.PolicyName,
            post.GetCustomAttribute<RateLimitAttribute>()!.PolicyName
        );
        Assert.Equal(
            get.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes.OrderBy(a => a),
            post.GetCustomAttribute<AllowActorTypesAttribute>()!.ActorTypes.OrderBy(a => a)
        );
        Assert.Equal(get.ReturnType, post.ReturnType);
    }

    private static CustomerController BuildController(RecordingBus bus, bool canViewAll, bool withIdentity = true)
    {
        var claims = withIdentity
            ? new List<Claim> { new(ClaimNames.TenantId, TenantId.ToString()), new("sub", UserId.ToString()) }
            : [];

        return new CustomerController(bus, new FixedPermissions(canViewAll))
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

    /// <summary>Captura lo invocado y responde una página vacía.</summary>
    private sealed class RecordingBus : IMessageBus
    {
        public List<object> Invoked { get; } = [];

        public PagedResult<CustomerSummaryResponse> Result { get; } = new([], 1, 20, 0);

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            Invoked.Add(message);
            return Task.FromResult((T)(object)Result);
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
