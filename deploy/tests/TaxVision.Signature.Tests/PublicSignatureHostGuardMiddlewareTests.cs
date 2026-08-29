using BuildingBlocks.Results;
using Microsoft.AspNetCore.Http;
using TaxVision.Signature.Api.Middleware;
using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Tests;

public class PublicSignatureHostGuardMiddlewareTests
{
    private const string Header = "X-Resolved-Tenant";

    private static DefaultHttpContext Context(string path, string? resolvedTenant)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (resolvedTenant is not null)
            ctx.Request.Headers[Header] = resolvedTenant;
        return ctx;
    }

    private static async Task<(int Status, bool NextCalled)> Run(HttpContext ctx, ISigningTokenService tokenService)
    {
        var nextCalled = false;
        var mw = new PublicSignatureHostGuardMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        await mw.InvokeAsync(ctx, tokenService);
        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task Blocks_with_403_when_host_tenant_differs_from_token_tenant()
    {
        var tokenTenant = Guid.NewGuid();
        var otherOffice = Guid.NewGuid();
        var ctx = Context("/signature/public/tok/sign", otherOffice.ToString());

        var (status, nextCalled) = await Run(ctx, new StubTokenService(tokenTenant));

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.False(nextCalled);
    }

    [Fact]
    public async Task Passes_when_host_tenant_matches_token_tenant()
    {
        var tenant = Guid.NewGuid();
        var ctx = Context("/signature/public/tok/sign", tenant.ToString());

        var (_, nextCalled) = await Run(ctx, new StubTokenService(tenant));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Passes_when_no_resolved_tenant_header_is_present()
    {
        var ctx = Context("/signature/public/tok/sign", resolvedTenant: null);

        var (_, nextCalled) = await Run(ctx, new StubTokenService(Guid.NewGuid()));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Ignores_non_public_paths()
    {
        var ctx = Context("/signature/requests", Guid.NewGuid().ToString());

        var (_, nextCalled) = await Run(ctx, new StubTokenService(Guid.NewGuid()));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Does_not_mask_an_invalid_token_with_403()
    {
        var ctx = Context("/signature/public/tok/sign", Guid.NewGuid().ToString());

        var (status, nextCalled) = await Run(ctx, new StubTokenService(Guid.NewGuid(), valid: false));

        Assert.NotEqual(StatusCodes.Status403Forbidden, status);
        Assert.True(nextCalled);
    }

    private sealed class StubTokenService(Guid tenantId, bool valid = true) : ISigningTokenService
    {
        public string Issue(SigningTokenPayload payload) => "stub";

        public Result<SigningTokenPayload> Verify(string token) =>
            valid
                ? Result.Success(
                    new SigningTokenPayload(
                        tenantId,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        0,
                        DateTime.UtcNow.AddDays(1),
                        "jti"
                    )
                )
                : Result.Failure<SigningTokenPayload>(new Error("Signature.Token.Format", "bad token"));
    }
}
