using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Domain;

/// <summary>
/// Cobertura acotada al nuevo parámetro <c>bulkRateLimitPerMinute</c> — el resto de la validación de
/// conexión ya se ejerce indirectamente vía <c>ProviderResolverTests</c>/<c>UpsertTenantEmailProviderHandlerTests</c>.
/// </summary>
public sealed class TenantEmailProviderTests
{
    private static TenantEmailProvider CreateValidProvider() =>
        TenantEmailProvider
            .Create(
                tenantId: Guid.NewGuid(),
                providerCode: "tenant-smtp",
                displayName: "Tenant SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "billing@tenant.example",
                fromDisplayNameDefault: "Tenant Corp",
                host: "smtp.tenant.example",
                port: 587,
                useTls: true,
                username: "tenant-user",
                passwordCipher: "tenant-secret",
                rateLimitPerMinute: 30,
                createdByUserId: Guid.NewGuid(),
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Create_leaves_BulkRateLimitPerMinute_null_when_not_provided()
    {
        var provider = CreateValidProvider();

        Assert.Null(provider.BulkRateLimitPerMinute);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_rejects_non_positive_bulk_rate_limit_when_provided(int invalidBulkLimit)
    {
        var result = TenantEmailProvider.Create(
            tenantId: Guid.NewGuid(),
            providerCode: "tenant-smtp",
            displayName: "Tenant SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "billing@tenant.example",
            fromDisplayNameDefault: null,
            host: "smtp.tenant.example",
            port: 587,
            useTls: true,
            username: null,
            passwordCipher: null,
            rateLimitPerMinute: 30,
            createdByUserId: Guid.NewGuid(),
            createdAtUtc: DateTime.UtcNow,
            bulkRateLimitPerMinute: invalidBulkLimit
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailProvider.BulkRateLimitPerMinute", result.Error.Code);
    }

    [Fact]
    public void UpdateConnection_can_set_bulk_rate_limit_independently_of_transactional()
    {
        var provider = CreateValidProvider();

        var result = provider.UpdateConnection(
            host: provider.Host,
            port: provider.Port,
            useTls: provider.UseTls,
            username: provider.Username,
            passwordCipher: null,
            fromAddressDefault: provider.FromAddressDefault,
            fromDisplayNameDefault: provider.FromDisplayNameDefault,
            rateLimitPerMinute: 30,
            updatedAtUtc: DateTime.UtcNow,
            bulkRateLimitPerMinute: 5
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(30, provider.RateLimitPerMinute);
        Assert.Equal(5, provider.BulkRateLimitPerMinute);
    }
}
