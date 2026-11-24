using BuildingBlocks.Platform;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Common;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.OAuth;

namespace TaxVision.Connectors.Tests.OAuth;

public sealed class InitiateOAuthConnectHandlerTests
{
    private readonly RecordingStateStore _states = new();

    [Fact]
    public async Task ReturnUrlWithPipes_IsRejectedAndNoStateIsCreated()
    {
        var result = await InitiateAsync("https://manfer.taxoffix.io|1|1", "https://app.taxoffix.io", "taxoffix.io");

        Assert.True(result.IsFailure);
        Assert.Equal(OAuthReturnOrigin.NotAllowedCode, result.Error.Code);
        Assert.Empty(_states.Created);
    }

    [Fact]
    public async Task ForeignReturnUrl_IsRejected()
    {
        var result = await InitiateAsync("https://evil.com", "https://app.taxoffix.io", "taxoffix.io");

        Assert.True(result.IsFailure);
        Assert.Empty(_states.Created);
    }

    [Fact]
    public async Task TenantSubdomain_IsStoredAsTheOriginOnly()
    {
        var result = await InitiateAsync("https://manfer.taxoffix.io/", "https://app.taxoffix.io", "taxoffix.io");

        Assert.True(result.IsSuccess);
        Assert.Equal("https://manfer.taxoffix.io", Assert.Single(_states.Created).ReturnOrigin);
    }

    // En local el CRM vuelve a su puerto de localhost.
    [Fact]
    public async Task LocalFrontendPort_IsStoredInDevelopment()
    {
        var result = await InitiateAsync("http://localhost:4200", "http://localhost:4200", "localhost");

        Assert.True(result.IsSuccess);
        Assert.Equal("http://localhost:4200", Assert.Single(_states.Created).ReturnOrigin);
    }

    // El header Origin no lo eligió quien llama: si no sirve se ignora y el callback usa el BaseUrl.
    [Fact]
    public async Task InvalidOriginHeader_IsIgnored()
    {
        var result = await InitiateAsync(
            "https://evil.com",
            "https://app.taxoffix.io",
            "taxoffix.io",
            fromHeader: true
        );

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(_states.Created).ReturnOrigin);
    }

    private Task<BuildingBlocks.Results.Result<InitiateOAuthConnectResult>> InitiateAsync(
        string returnOrigin,
        string baseUrl,
        string platformDomain,
        bool fromHeader = false
    ) =>
        InitiateOAuthConnectHandler.Handle(
            new InitiateOAuthConnectCommand(
                Guid.NewGuid(),
                ProviderCode.Gmail,
                Guid.NewGuid(),
                "owner@example.test",
                returnOrigin,
                ReturnOriginFromHeader: fromHeader
            ),
            new FakeOAuthProviderClientFactory(new FakeOAuthProviderClient(ProviderCode.Gmail)),
            _states,
            Options.Create(new ConnectorsPortalOptions { BaseUrl = baseUrl }),
            new TestPlatform(platformDomain),
            CancellationToken.None
        );

    private sealed class RecordingStateStore : IOAuthConnectStateStore
    {
        private readonly InMemoryOAuthConnectStateStore _inner = new();
        public List<OAuthConnectState> Created { get; } = [];

        public Task<string> CreateAsync(
            Guid tenantId,
            ProviderCode providerCode,
            Guid initiatedByUserId,
            string? initiatorEmail = null,
            string? returnOrigin = null,
            bool asOffice = false,
            bool initiatorIsPlatformAdmin = false,
            CancellationToken ct = default
        )
        {
            Created.Add(
                new OAuthConnectState(
                    tenantId,
                    providerCode,
                    initiatedByUserId,
                    initiatorEmail,
                    returnOrigin,
                    asOffice,
                    initiatorIsPlatformAdmin
                )
            );
            return _inner.CreateAsync(
                tenantId,
                providerCode,
                initiatedByUserId,
                initiatorEmail,
                returnOrigin,
                asOffice,
                initiatorIsPlatformAdmin,
                ct
            );
        }

        public Task<OAuthConnectState?> ConsumeAsync(string state, CancellationToken ct = default) =>
            _inner.ConsumeAsync(state, ct);
    }
}

internal sealed class TestPlatform(string baseDomain) : IPlatformSettings
{
    public PlatformSettingsSnapshot Current { get; } =
        new(
            1,
            "TaxVision",
            "TaxVision LLC",
            baseDomain,
            "api." + baseDomain,
            "support@example.test",
            "billing@example.test"
        );
}
