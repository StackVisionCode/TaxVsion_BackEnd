using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TermsVersions.Commands;
using TaxVision.Auth.Domain.Onboarding.TermsVersions;

namespace TaxVision.Auth.Application.Onboarding.TermsVersions.Queries;

public sealed record GetCurrentTermsVersionQuery(TermsKind Kind, string Locale);

public static class GetCurrentTermsVersionHandler
{
    public static async Task<Result<TermsVersionResponse>> Handle(
        GetCurrentTermsVersionQuery query,
        ITermsVersionRepository repository,
        CancellationToken ct
    )
    {
        var current = await repository.GetCurrentAsync(query.Kind, query.Locale, DateTime.UtcNow, ct);
        if (current is null)
            return Result.Failure<TermsVersionResponse>(
                new Error(
                    "TermsVersion.NotFound",
                    "No published terms version was found for the given kind and locale."
                )
            );

        return Result.Success(
            new TermsVersionResponse(
                current.Id,
                current.Kind,
                current.Version,
                current.ContentUri,
                current.ContentHash,
                current.Locale,
                current.EffectiveFromUtc,
                current.EffectiveUntilUtc
            )
        );
    }
}
