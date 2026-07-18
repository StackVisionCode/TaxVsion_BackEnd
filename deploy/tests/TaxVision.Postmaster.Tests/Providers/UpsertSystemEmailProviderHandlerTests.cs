using TaxVision.Postmaster.Application.Providers.Commands.UpsertSystemEmailProvider;
using TaxVision.Postmaster.Domain.Providers;
using TaxVision.Postmaster.Tests.Consumers;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class UpsertSystemEmailProviderHandlerTests
{
    private static UpsertSystemEmailProviderCommand CreateCommand(string providerCode = "smtp-default") =>
        new(
            providerCode,
            "Corporate SMTP",
            EmailProviderType.Smtp,
            "no-reply@taxvision.com",
            "TaxVision",
            "smtp.sendgrid.net",
            587,
            true,
            "apikey",
            "real-secret",
            120
        );

    [Fact]
    public async Task Handle_creates_provider_when_none_exists_for_code()
    {
        var repository = new FakeSystemEmailProviderRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await UpsertSystemEmailProviderHandler.Handle(
            CreateCommand(),
            repository,
            new FakeSecretProtector(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var stored = await repository.GetByCodeAsync("smtp-default", CancellationToken.None);
        Assert.True(stored.IsSuccess);
        Assert.Equal("smtp.sendgrid.net", stored.Value.Host);
        Assert.Equal("real-secret", stored.Value.PasswordCipher!.Cipher);
    }

    [Fact]
    public async Task Handle_reconfigures_existing_provider_instead_of_duplicating()
    {
        var repository = new FakeSystemEmailProviderRepository();
        var unitOfWork = new FakeUnitOfWork();
        await UpsertSystemEmailProviderHandler.Handle(
            CreateCommand(),
            repository,
            new FakeSecretProtector(),
            unitOfWork,
            CancellationToken.None
        );

        var updateCommand = CreateCommand() with { Host = "smtp2.sendgrid.net", RateLimitPerMinute = 300 };
        var result = await UpsertSystemEmailProviderHandler.Handle(
            updateCommand,
            repository,
            new FakeSecretProtector(),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var stored = await repository.GetByCodeAsync("smtp-default", CancellationToken.None);
        Assert.Equal("smtp2.sendgrid.net", stored.Value.Host);
        Assert.Equal(300, stored.Value.RateLimitPerMinute);
    }

    [Fact]
    public async Task Handle_fails_when_smtp_host_missing_for_new_provider()
    {
        var repository = new FakeSystemEmailProviderRepository();
        var command = CreateCommand() with { Host = null };

        var result = await UpsertSystemEmailProviderHandler.Handle(
            command,
            repository,
            new FakeSecretProtector(),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SystemEmailProvider.Host", result.Error.Code);
    }
}
