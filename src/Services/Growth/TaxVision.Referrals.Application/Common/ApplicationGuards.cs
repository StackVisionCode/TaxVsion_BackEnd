using BuildingBlocks.Results;

namespace TaxVision.Referrals.Application.Common;

internal static class ApplicationGuards
{
    public static Result EnsureActor(Guid actorUserId) =>
        actorUserId == Guid.Empty
            ? Result.Failure(new Error("Referrals.InvalidActor", "ActorUserId is required."))
            : Result.Success();

    public static Result IdempotencyConflict() =>
        Result.Failure(
            new Error(
                "Referrals.IdempotencyConflict",
                "The idempotency key was already used with a different payload."
            )
        );

    public static Result OperationInProgress() =>
        Result.Failure(
            new Error(
                "Referrals.OperationInProgress",
                "An operation with this idempotency key is still in progress."
            )
        );
}
