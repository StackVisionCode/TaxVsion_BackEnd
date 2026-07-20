using BuildingBlocks.Results;

namespace TaxVision.Codes.Domain.ValueObjects;

public sealed record OfferReference
{
    public string Owner { get; }
    public string OfferId { get; }
    public string OfferVersion { get; }

    private OfferReference(string owner, string offerId, string offerVersion)
    {
        Owner = owner;
        OfferId = offerId;
        OfferVersion = offerVersion;
    }

    public static Result<OfferReference> Create(string owner, string offerId, string offerVersion)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Trim().Length > 100)
            return Result.Failure<OfferReference>(
                new Error("Codes.OfferReference.InvalidOwner", "Offer owner is required and cannot exceed 100 characters.")
            );

        if (string.IsNullOrWhiteSpace(offerId) || offerId.Trim().Length > 200)
            return Result.Failure<OfferReference>(
                new Error("Codes.OfferReference.InvalidId", "OfferId is required and cannot exceed 200 characters.")
            );

        if (string.IsNullOrWhiteSpace(offerVersion) || offerVersion.Trim().Length > 100)
            return Result.Failure<OfferReference>(
                new Error(
                    "Codes.OfferReference.InvalidVersion",
                    "OfferVersion is required and cannot exceed 100 characters."
                )
            );

        return Result.Success(new OfferReference(owner.Trim(), offerId.Trim(), offerVersion.Trim()));
    }
}
