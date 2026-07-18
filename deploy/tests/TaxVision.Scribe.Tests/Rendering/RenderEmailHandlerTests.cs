using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Tests.Rendering;

/// <summary>Fase 7 — entry point único de Application usado por el HTTP /scribe/render (el gRPC TemplateService equivalente se retiró en la Fase 8 del hardening, ver ADR-0003).</summary>
public sealed class RenderEmailHandlerTests
{
    private sealed class FakeEmailRenderer(Result<RenderedContent> result) : IEmailRenderer
    {
        public RenderRequest? LastRequest { get; private set; }

        public Task<Result<RenderedContent>> RenderAsync(RenderRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(result);
        }

        public Task<Result<RenderedContent>> PreviewAsync(
            Guid versionId,
            IReadOnlyDictionary<string, object?> sampleVariables,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<Result> WarmupAsync(
            Domain.Templates.EmailTemplateVersion version,
            string templateKeyValue,
            Domain.Layouts.EmailLayoutVersion layoutVersion,
            string layoutKeyValue,
            Guid? tenantId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Handle_rejects_an_invalid_event_key_without_calling_the_renderer()
    {
        var renderer = new FakeEmailRenderer(Result.Success(new RenderedContent("s", "h", "t", [])));
        var query = new RenderEmailQuery("Invalid Key!!", null, null, new Dictionary<string, object?>());

        var result = await RenderEmailHandler.Handle(query, renderer, default);

        Assert.True(result.IsFailure);
        Assert.Equal("EventKey.Format", result.Error.Code);
        Assert.Null(renderer.LastRequest);
    }

    [Fact]
    public async Task Handle_rejects_an_invalid_locale()
    {
        var renderer = new FakeEmailRenderer(Result.Success(new RenderedContent("s", "h", "t", [])));
        var query = new RenderEmailQuery(
            "auth.password_reset_requested.v1",
            null,
            "not-a-locale!!",
            new Dictionary<string, object?>()
        );

        var result = await RenderEmailHandler.Handle(query, renderer, default);

        Assert.True(result.IsFailure);
        Assert.Null(renderer.LastRequest);
    }

    [Fact]
    public async Task Handle_delegates_to_the_renderer_with_the_mapped_request()
    {
        var expected = new RenderedContent("Subject", "<p>Html</p>", "Text", []);
        var renderer = new FakeEmailRenderer(Result.Success(expected));
        var tenantId = Guid.NewGuid();
        var query = new RenderEmailQuery(
            "auth.password_reset_requested.v1",
            tenantId,
            "en-US",
            new Dictionary<string, object?> { ["name"] = "Ana" },
            LogoScope.Tenant
        );

        var result = await RenderEmailHandler.Handle(query, renderer, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
        Assert.NotNull(renderer.LastRequest);
        Assert.Equal("auth.password_reset_requested.v1", renderer.LastRequest!.EventKey.Value);
        Assert.Equal(tenantId, renderer.LastRequest.TenantId);
        Assert.Equal("en-US", renderer.LastRequest.Locale?.Value);
        Assert.Equal(LogoScope.Tenant, renderer.LastRequest.LogoScope);
    }

    [Fact]
    public async Task Handle_propagates_a_render_failure()
    {
        var renderer = new FakeEmailRenderer(
            Result.Failure<RenderedContent>(new Error("EmailRenderer.NoMapping", "No mapping."))
        );
        var query = new RenderEmailQuery(
            "auth.password_reset_requested.v1",
            null,
            null,
            new Dictionary<string, object?>()
        );

        var result = await RenderEmailHandler.Handle(query, renderer, default);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailRenderer.NoMapping", result.Error.Code);
    }
}
