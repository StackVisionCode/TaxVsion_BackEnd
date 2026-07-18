using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Tests.Domain;

public sealed class SystemEmailProviderTests
{
    private static SystemEmailProvider CreateValidProvider() =>
        SystemEmailProvider
            .Create(
                providerCode: "smtp-default",
                displayName: "Default SMTP",
                providerType: EmailProviderType.Smtp,
                fromAddressDefault: "no-reply@taxvision.local",
                fromDisplayNameDefault: "TaxVision",
                host: "localhost",
                port: 1025,
                useTls: false,
                username: null,
                passwordCipher: "cipher-placeholder",
                rateLimitPerMinute: 60,
                createdAtUtc: DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void Create_succeeds_with_valid_smtp_data()
    {
        var result = SystemEmailProvider.Create(
            providerCode: "smtp-default",
            displayName: "Default SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "no-reply@taxvision.local",
            fromDisplayNameDefault: "TaxVision",
            host: "localhost",
            port: 1025,
            useTls: false,
            username: null,
            passwordCipher: null,
            rateLimitPerMinute: 60,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Enabled);
        Assert.Equal("no-reply@taxvision.local", result.Value.FromAddressDefault);
    }

    [Fact]
    public void Create_rejects_smtp_provider_without_host()
    {
        var result = SystemEmailProvider.Create(
            providerCode: "smtp-default",
            displayName: "Default SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "no-reply@taxvision.local",
            fromDisplayNameDefault: null,
            host: null,
            port: null,
            useTls: false,
            username: null,
            passwordCipher: null,
            rateLimitPerMinute: 60,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemEmailProvider.Host", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_invalid_from_address()
    {
        var result = SystemEmailProvider.Create(
            providerCode: "smtp-default",
            displayName: "Default SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "not-an-email",
            fromDisplayNameDefault: null,
            host: "localhost",
            port: 1025,
            useTls: false,
            username: null,
            passwordCipher: null,
            rateLimitPerMinute: 60,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemEmailProvider.FromAddressDefault", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_non_positive_rate_limit()
    {
        var result = SystemEmailProvider.Create(
            providerCode: "smtp-default",
            displayName: "Default SMTP",
            providerType: EmailProviderType.Smtp,
            fromAddressDefault: "no-reply@taxvision.local",
            fromDisplayNameDefault: null,
            host: "localhost",
            port: 1025,
            useTls: false,
            username: null,
            passwordCipher: null,
            rateLimitPerMinute: 0,
            createdAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemEmailProvider.RateLimitPerMinute", result.Error.Code);
    }

    [Fact]
    public void UpdateConnection_replaces_connection_fields()
    {
        var provider = CreateValidProvider();

        var result = provider.UpdateConnection(
            host: "smtp.new-host.com",
            port: 587,
            useTls: true,
            username: "user@new-host.com",
            passwordCipher: "new-cipher",
            fromAddressDefault: "updated@taxvision.local",
            fromDisplayNameDefault: "TaxVision Updated",
            rateLimitPerMinute: 120,
            updatedAtUtc: DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("smtp.new-host.com", provider.Host);
        Assert.True(provider.UseTls);
        Assert.Equal("updated@taxvision.local", provider.FromAddressDefault);
        Assert.Equal(120, provider.RateLimitPerMinute);
        Assert.NotNull(provider.UpdatedAtUtc);
    }

    [Fact]
    public void Enable_and_Disable_toggle_state()
    {
        var provider = CreateValidProvider();

        provider.Disable(DateTime.UtcNow);
        Assert.False(provider.Enabled);

        provider.Enable(DateTime.UtcNow);
        Assert.True(provider.Enabled);
    }
}
