using TaxVision.Notification.Domain.Emailing.Configurations;

namespace TaxVision.Notification.Tests.Domain;

public sealed class EmailConfigurationTests
{
    [Fact]
    public void System_configuration_must_not_carry_tenant()
    {
        var result = Create(ProviderScope.System, tenantId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("EmailConfiguration.Scope", result.Error.Code);
    }

    [Fact]
    public void Tenant_configuration_requires_tenant()
    {
        var result = Create(ProviderScope.Tenant, tenantId: null);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailConfiguration.Tenant", result.Error.Code);
    }

    [Fact]
    public void Smtp_configuration_requires_host()
    {
        var result = EmailProviderConfiguration.Create(
            ProviderScope.System,
            null,
            EmailProviderType.Smtp,
            "Global",
            "from@taxvision.com",
            null,
            host: null,
            port: 587,
            username: null,
            passwordCipher: null,
            useSsl: true,
            apiKeyCipher: null,
            clientId: null,
            clientSecretCipher: null,
            tenantProviderId: null,
            isDefault: false
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailConfiguration.Host", result.Error.Code);
    }

    [Fact]
    public void Update_keeps_existing_secret_when_cipher_is_null()
    {
        var config = Create(ProviderScope.System, null, passwordCipher: "cipher-1").Value;

        var result = config.Update(
            "Global",
            "from@taxvision.com",
            null,
            "smtp.taxvision.com",
            587,
            "user",
            passwordCipher: null,
            useSsl: true,
            apiKeyCipher: null,
            clientId: null,
            clientSecretCipher: null,
            tenantProviderId: null
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("cipher-1", config.PasswordCipher);
    }

    [Fact]
    public void Deactivate_clears_default_flag()
    {
        var config = Create(ProviderScope.System, null, isDefault: true).Value;

        config.Deactivate();

        Assert.False(config.IsActive);
        Assert.False(config.IsDefault);
    }

    private static BuildingBlocks.Results.Result<EmailProviderConfiguration> Create(
        ProviderScope scope,
        Guid? tenantId,
        string? passwordCipher = null,
        bool isDefault = false
    ) =>
        EmailProviderConfiguration.Create(
            scope,
            tenantId,
            EmailProviderType.Smtp,
            "Config",
            "from@taxvision.com",
            "TaxVision",
            host: "smtp.taxvision.com",
            port: 587,
            username: "user",
            passwordCipher: passwordCipher,
            useSsl: true,
            apiKeyCipher: null,
            clientId: null,
            clientSecretCipher: null,
            tenantProviderId: null,
            isDefault: isDefault
        );
}
