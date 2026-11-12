using BuildingBlocks.Common;
using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;

namespace TaxVision.Codes.Application.Definitions.Admin;

public sealed record ListPlatformCodesQuery(int Page = 1, int Size = 20, string? Status = null);

public sealed record ListCodeRedemptionsQuery(Guid CodeDefinitionId, int Page = 1, int Size = 20);

/// <summary>Un canje confirmado de una giftcard: quién pagó, cuánto se descontó y cuándo.</summary>
public sealed record CodeRedemptionRow(
    Guid RedemptionId,
    Guid CodeDefinitionId,
    Guid ConsumerTenantId,
    Guid PaymentId,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    bool WasLateCommit,
    DateTime CommittedAtUtc
);

public static class CodeAdminQueryHandler
{
    private static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Draft",
        "Active",
        "Suspended",
        "Revoked",
        "Expired",
    };

    public static async Task<Result<PagedResult<CodeDefinitionDetailsResponse>>> Handle(
        ListPlatformCodesQuery query,
        ICodeAdminReadModel readModel,
        CancellationToken ct
    )
    {
        if (Paging(query.Page, query.Size) is { } invalid)
            return Result.Failure<PagedResult<CodeDefinitionDetailsResponse>>(invalid);

        if (!string.IsNullOrWhiteSpace(query.Status) && !Statuses.Contains(query.Status.Trim()))
        {
            return Result.Failure<PagedResult<CodeDefinitionDetailsResponse>>(
                new Error("Codes.ListCodes.InvalidStatus", $"Unknown status '{query.Status}'.")
            );
        }

        return Result.Success(
            await readModel.ListPlatformDefinitionsAsync(query.Page, query.Size, query.Status?.Trim(), ct)
        );
    }

    public static async Task<Result<PagedResult<CodeRedemptionRow>>> Handle(
        ListCodeRedemptionsQuery query,
        ICodeAdminReadModel readModel,
        CancellationToken ct
    )
    {
        if (query.CodeDefinitionId == Guid.Empty)
        {
            return Result.Failure<PagedResult<CodeRedemptionRow>>(
                new Error("Codes.ListRedemptions.InvalidDefinition", "CodeDefinitionId is required.")
            );
        }

        if (Paging(query.Page, query.Size) is { } invalid)
            return Result.Failure<PagedResult<CodeRedemptionRow>>(invalid);

        var rows = await readModel.ListRedemptionsAsync(query.CodeDefinitionId, query.Page, query.Size, ct);
        return rows is null
            ? Result.Failure<PagedResult<CodeRedemptionRow>>(
                new Error("Codes.ListRedemptions.NotFound", "The gift card does not exist.")
            )
            : Result.Success(rows);
    }

    private static Error? Paging(int page, int size) =>
        page < 1 || size is < 1 or > 100
            ? new Error("Codes.Admin.InvalidPaging", "page must be at least 1 and size between 1 and 100.")
            : null;
}
