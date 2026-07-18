using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers.Commands.UpsertSystemEmailProvider;

public static class UpsertSystemEmailProviderHandler
{
    public static async Task<Result> Handle(
        UpsertSystemEmailProviderCommand cmd,
        ISystemEmailProviderRepository repository,
        ISecretProtector secretProtector,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var passwordCipher = cmd.Password is null ? null : secretProtector.Protect(cmd.Password);
        var existing = await repository.GetByCodeAsync(cmd.ProviderCode, ct);

        var result = existing.IsSuccess
            ? UpdateExisting(existing.Value, cmd, passwordCipher)
            : await CreateNew(cmd, passwordCipher, repository, ct);

        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static Result UpdateExisting(
        SystemEmailProvider provider,
        UpsertSystemEmailProviderCommand cmd,
        string? passwordCipher
    ) =>
        provider.UpdateConnection(
            cmd.Host,
            cmd.Port,
            cmd.UseTls,
            cmd.Username,
            passwordCipher,
            cmd.FromAddressDefault,
            cmd.FromDisplayNameDefault,
            cmd.RateLimitPerMinute,
            DateTime.UtcNow
        );

    private static async Task<Result> CreateNew(
        UpsertSystemEmailProviderCommand cmd,
        string? passwordCipher,
        ISystemEmailProviderRepository repository,
        CancellationToken ct
    )
    {
        var createResult = SystemEmailProvider.Create(
            cmd.ProviderCode,
            cmd.DisplayName,
            cmd.ProviderType,
            cmd.FromAddressDefault,
            cmd.FromDisplayNameDefault,
            cmd.Host,
            cmd.Port,
            cmd.UseTls,
            cmd.Username,
            passwordCipher,
            cmd.RateLimitPerMinute,
            DateTime.UtcNow
        );
        if (createResult.IsFailure)
            return Result.Failure(createResult.Error);

        await repository.AddAsync(createResult.Value, ct);
        return Result.Success();
    }
}
