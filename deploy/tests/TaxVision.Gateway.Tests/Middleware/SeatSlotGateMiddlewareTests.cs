using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TaxVision.Gateway.Tests.Middleware;

public sealed class SeatSlotGateMiddlewareTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Disabled_does_not_query_projection()
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Disabled, gate);

        Assert.True(result.ReachedNext);
        Assert.Equal(0, gate.Calls);
    }

    [Fact]
    public async Task Shadow_observes_without_blocking()
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Shadow, gate, isDevelopment: true);

        Assert.True(result.ReachedNext);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("true", result.Context.Response.Headers["X-Seat-Slot-Gated"]);
    }

    [Fact]
    public async Task Enforce_returns_structured_403()
    {
        var result = await InvokeAsync(SlotGatingMode.Enforce, new FakeGate(GatedDecision()));

        Assert.False(result.ReachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal("seats.user_slot_gated", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(11, body.RootElement.GetProperty("slotIndex").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("effectiveCap").GetInt32());
        Assert.Equal(
            "https://office.taxproffice.com/account/users",
            body.RootElement.GetProperty("contactAdminUrl").GetString()
        );
    }

    [Theory]
    [InlineData("/auth/me")]
    [InlineData("/auth/me/access")]
    [InlineData("/auth/logout")]
    [InlineData("/health/ready")]
    [InlineData("/internal/reconcile")]
    public async Task Exempt_path_never_queries_projection(string path)
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Enforce, gate, path: path);

        Assert.True(result.ReachedNext);
        Assert.Equal(0, gate.Calls);
    }

    [Theory]
    [InlineData(ActorType.CustomerPortal)]
    [InlineData(ActorType.PlatformAdmin)]
    [InlineData(ActorType.Service)]
    public async Task Exempt_actor_never_queries_projection(ActorType actorType)
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Enforce, gate, actorType: actorType);

        Assert.True(result.ReachedNext);
        Assert.Equal(0, gate.Calls);
    }

    [Fact]
    public async Task Owner_never_queries_projection()
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Enforce, gate, isOwner: true);

        Assert.True(result.ReachedNext);
        Assert.Equal(0, gate.Calls);
    }

    [Fact]
    public async Task Anonymous_endpoint_never_queries_projection()
    {
        var gate = new FakeGate(GatedDecision());

        var result = await InvokeAsync(SlotGatingMode.Enforce, gate, allowAnonymous: true);

        Assert.True(result.ReachedNext);
        Assert.Equal(0, gate.Calls);
    }

    [Fact]
    public async Task Projection_failure_does_not_turn_into_500()
    {
        var result = await InvokeAsync(SlotGatingMode.Enforce, new FakeGate(new InvalidOperationException("down")));

        Assert.True(result.ReachedNext);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    private static SeatSlotDecision GatedDecision() => new(false, "seats.user_slot_gated", 11, 10);

    private static async Task<InvocationResult> InvokeAsync(
        SlotGatingMode mode,
        FakeGate gate,
        string path = "/customers",
        ActorType actorType = ActorType.TenantEmployee,
        bool isOwner = false,
        bool allowAnonymous = false,
        bool isDevelopment = false
    )
    {
        var reachedNext = false;
        var middleware = new SeatSlotGateMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            new StaticOptionsMonitor<SlotGatingOptions>(new SlotGatingOptions { Mode = mode }),
            new TestHostEnvironment(isDevelopment),
            NullLogger<SeatSlotGateMiddleware>.Instance
        );

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("office.taxproffice.com");
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        var claims = new List<Claim>
        {
            new("sub", UserId.ToString()),
            new(ClaimNames.TenantId, TenantId.ToString()),
            new(ClaimNames.ActorType, actorType.ToString()),
        };
        if (isOwner)
            claims.Add(new Claim(ClaimNames.IsOwner, "true"));
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

        if (allowAnonymous)
            context.SetEndpoint(
                new Endpoint(
                    _ => Task.CompletedTask,
                    new EndpointMetadataCollection(new AllowAnonymousAttribute()),
                    "anonymous"
                )
            );

        await middleware.InvokeAsync(context, gate);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new InvocationResult(context, context.Response.StatusCode, reachedNext, body);
    }

    private sealed record InvocationResult(HttpContext Context, int StatusCode, bool ReachedNext, string Body);

    private sealed class FakeGate : ISeatSlotGate
    {
        private readonly SeatSlotDecision? _decision;
        private readonly Exception? _exception;

        public FakeGate(SeatSlotDecision decision) => _decision = decision;

        public FakeGate(Exception exception) => _exception = exception;

        public int Calls { get; private set; }

        public ValueTask<SeatSlotDecision> CheckAsync(Guid tenantId, Guid userId, CancellationToken ct)
        {
            Calls++;
            return _exception is not null
                ? ValueTask.FromException<SeatSlotDecision>(_exception)
                : ValueTask.FromResult(_decision!);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class TestHostEnvironment(bool isDevelopment) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            isDevelopment ? Environments.Development : Environments.Production;
        public string ApplicationName { get; set; } = "TaxVision.Gateway.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
