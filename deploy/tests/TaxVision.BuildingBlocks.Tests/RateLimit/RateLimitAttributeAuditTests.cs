using System.Security.Claims;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.RateLimit;

// El 429 se audita en M y en las políticas que lo piden; el resto sigue sin auditar.
[Collection(RateLimitMetricsCollection.Name)]
public sealed class RateLimitAttributeAuditTests
{
    private sealed class ExceededEvaluator : ITieredRateLimitEvaluator
    {
        public Task<RateLimitVerdict> EvaluateAsync(
            RateLimitPolicyDefinition policy,
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default,
            string? partitionValue = null
        ) => Task.FromResult(RateLimitVerdict.Exceeded("user", policy.BaseQuotaPerMinute, 60));
    }

    private sealed class SpySink : IRateLimitAuditSink
    {
        public List<RateLimitAuditContext> Blocked { get; } = [];

        public Task OnBlockedAsync(RateLimitAuditContext context, CancellationToken ct = default)
        {
            Blocked.Add(context);
            return Task.CompletedTask;
        }
    }

    private static async Task<(SpySink Sink, HttpContext Http)> RunBlockedAsync(string policyName)
    {
        var sink = new SpySink();
        var services = new ServiceCollection()
            .AddSingleton<IRateLimitPolicyRegistry, RateLimitPolicyRegistry>()
            .AddSingleton<ITieredRateLimitEvaluator, ExceededEvaluator>()
            .AddSingleton<IRateLimitAuditSink>(sink)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Response.Body = new MemoryStream();
        http.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("tenant_id", Guid.NewGuid().ToString()), new Claim("sub", Guid.NewGuid().ToString())],
                "test"
            )
        );
        var context = new ResourceExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [],
            []
        );

        await new RateLimitAttribute(policyName).OnResourceExecutionAsync(
            context,
            () => throw new InvalidOperationException("A blocked request must not reach the action.")
        );

        return (sink, http);
    }

    [Theory]
    [InlineData("auth.n.credential_admin")]
    [InlineData("customer.n.portal_credential_admin")]
    public async Task Credential_administration_429_is_audited(string policyName)
    {
        var (sink, http) = await RunBlockedAsync(policyName);

        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);
        var blocked = Assert.Single(sink.Blocked);
        Assert.Equal(policyName, blocked.PolicyName);
    }

    [Fact]
    public async Task Existing_n_policy_without_the_flag_keeps_its_behavior()
    {
        var (sink, http) = await RunBlockedAsync("customer.n.fiscal_reveal");

        Assert.Equal(StatusCodes.Status429TooManyRequests, http.Response.StatusCode);
        Assert.Empty(sink.Blocked);
    }
}
