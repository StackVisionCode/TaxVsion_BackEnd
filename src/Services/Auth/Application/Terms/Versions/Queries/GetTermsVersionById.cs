using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Terms.Versions.Commands;

namespace TaxVision.Auth.Application.Terms.Versions.Queries;

public sealed record GetTermsVersionByIdQuery(Guid TermsVersionId);

public static class GetTermsVersionByIdHandler
{
    public static async Task<Result<TermsVersionResponse>> Handle(
        GetTermsVersionByIdQuery query,
        ITermsVersionRepository repository,
        CancellationToken ct
    )
    {
        var version = await repository.GetByIdAsync(query.TermsVersionId, ct);
        if (version is null)
            return Result.Failure<TermsVersionResponse>(
                new Error("TermsVersion.NotFound", "The requested terms version does not exist.")
            );

        return Result.Success(
            new TermsVersionResponse(
                version.Id,
                version.Kind,
                version.Version,
                version.ContentUri,
                version.ContentHash,
                version.Locale,
                version.EffectiveFromUtc,
                version.EffectiveUntilUtc
            )
        );
    }
}
