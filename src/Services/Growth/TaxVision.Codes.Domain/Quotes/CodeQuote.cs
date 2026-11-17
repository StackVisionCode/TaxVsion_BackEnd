using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Quotes;

public sealed class CodeQuote : TenantEntity
{
    public Guid CodeDefinitionId { get; private set; }
    public Guid CodeRuleVersionId { get; private set; }
    public int RuleVersion { get; private set; }
    public CodeDisplay CodeDisplay { get; private set; } = null!;
    public SubjectReference Subject { get; private set; } = null!;

    /// <summary>Comprador (id de <c>BuyerIdentity</c>) cuando el llamador lo informó; los límites por sujeto cuentan por él.</summary>
    public string? BuyerSubjectId { get; private set; }

    /// <summary>Sujeto con el que se cuentan los límites por sujeto: el comprador si lo hay; si no, el sujeto.</summary>
    public SubjectReference LimitSubject =>
        BuyerSubjectId is null ? Subject : SubjectReference.Create(SubjectType.Buyer, BuyerSubjectId).Value;
    public OfferReference Offer { get; private set; } = null!;
    public Money GrossAmount { get; private set; } = null!;
    public Money DiscountAmount { get; private set; } = null!;
    public Money NetAmount { get; private set; } = null!;
    public SnapshotHash SnapshotHash { get; private set; } = null!;
    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public PayloadFingerprint PayloadFingerprint { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    private CodeQuote() { }

    internal static Result<CodeQuote> Create(
        Guid tenantId,
        Guid codeDefinitionId,
        Guid codeRuleVersionId,
        int ruleVersion,
        CodeDisplay codeDisplay,
        SubjectReference subject,
        OfferReference offer,
        Money grossAmount,
        Money discountAmount,
        Money netAmount,
        SnapshotHash snapshotHash,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        DateTime createdAtUtc,
        DateTime expiresAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CodeQuote>(new Error("Codes.CodeQuote.InvalidTenant", "TenantId is required."));

        if (codeDefinitionId == Guid.Empty || codeRuleVersionId == Guid.Empty || ruleVersion <= 0)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.InvalidRule", "Code definition and rule version are required.")
            );

        if (
            !string.Equals(grossAmount.Currency, discountAmount.Currency, StringComparison.Ordinal)
            || !string.Equals(grossAmount.Currency, netAmount.Currency, StringComparison.Ordinal)
        )
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.CurrencyMismatch", "Gross, discount, and net currencies must match.")
            );

        if (discountAmount.AmountCents > grossAmount.AmountCents)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.DiscountExceedsGross", "Discount cannot exceed gross amount.")
            );

        if (grossAmount.AmountCents - discountAmount.AmountCents != netAmount.AmountCents)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.InvalidNetAmount", "Net amount must equal gross minus discount.")
            );

        if (expiresAtUtc <= createdAtUtc)
            return Result.Failure<CodeQuote>(
                new Error("Codes.CodeQuote.InvalidExpiry", "ExpiresAtUtc must be after CreatedAtUtc.")
            );

        var quote = new CodeQuote
        {
            CodeDefinitionId = codeDefinitionId,
            CodeRuleVersionId = codeRuleVersionId,
            RuleVersion = ruleVersion,
            CodeDisplay = codeDisplay,
            Subject = subject,
            Offer = offer,
            GrossAmount = grossAmount,
            DiscountAmount = discountAmount,
            NetAmount = netAmount,
            SnapshotHash = snapshotHash,
            IdempotencyKey = idempotencyKey,
            PayloadFingerprint = payloadFingerprint,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
        quote.SetTenant(tenantId);
        return Result.Success(quote);
    }

    /// <summary>Asigna el comprador una sola vez (al crear la cotización).</summary>
    public Result AttachBuyer(string buyerSubjectId)
    {
        var buyer = SubjectReference.Create(SubjectType.Buyer, buyerSubjectId);
        if (buyer.IsFailure)
            return Result.Failure(buyer.Error);
        if (BuyerSubjectId is not null && BuyerSubjectId != buyer.Value.SubjectId)
            return Result.Failure(new Error("Codes.CodeQuote.BuyerAlreadySet", "The quote already has a buyer."));

        BuyerSubjectId = buyer.Value.SubjectId;
        return Result.Success();
    }

    public Result EnsureReservable(DateTime nowUtc)
    {
        if (nowUtc >= ExpiresAtUtc)
            return Result.Failure(new Error("Codes.CodeQuote.Expired", "Quote has expired."));

        return Result.Success();
    }
}
