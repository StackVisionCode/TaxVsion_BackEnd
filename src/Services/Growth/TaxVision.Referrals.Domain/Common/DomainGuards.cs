using BuildingBlocks.Results;

namespace TaxVision.Referrals.Domain.Common;

internal static class DomainGuards
{
    public static Result EnsureActor(Guid actorUserId) =>
        actorUserId == Guid.Empty
            ? Result.Failure(new Error("Referrals.InvalidActor", "ActorUserId is required."))
            : Result.Success();

    public static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));

    public static string NormalizeSha256Hex(string value) => value.ToLowerInvariant();
}
