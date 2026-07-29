using System.Security.Cryptography;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

public sealed class HttpTermsDocumentHasher(HttpClient httpClient) : ITermsDocumentHasher
{
    public async Task<Result<string>> ComputeHashAsync(string contentUri, CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(contentUri, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<string>(
                    new Error(
                        "Onboarding.TermsContentFetchFailed",
                        $"Fetching the terms document at {contentUri} returned HTTP {(int)response.StatusCode}."
                    )
                );

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return Result.Failure<string>(
                    new Error("Onboarding.TermsContentFetchFailed", $"The terms document at {contentUri} is empty.")
                );

            return Result.Success(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<string>(
                new Error("Onboarding.TermsContentFetchFailed", $"Failed to fetch {contentUri}: {ex.Message}")
            );
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<string>(
                new Error("Onboarding.TermsContentFetchFailed", $"Timed out fetching {contentUri}.")
            );
        }
    }
}
