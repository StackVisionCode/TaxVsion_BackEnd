using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.FinancialInsights.Domain.Imports;

/// <summary>Movimiento sugerido por el modelo. Siempre queda sujeto a revisión del staff antes de usarse.</summary>
public sealed class FinancialMovement : AggregateRoot, ITenantOwned
{
    public const string Credit = "credit";
    public const string Debit = "debit";
    public const decimal MaxAmount = 1_000_000_000m;
    public const int MaxDescriptionLength = 500;
    public const int MaxCategoryLength = 120;

    private FinancialMovement() { }

    public Guid CustomerId { get; private set; }
    public Guid ImportDocumentId { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "USD";
    public string Direction { get; private set; } = string.Empty;
    public string SuggestedCategory { get; private set; } = string.Empty;
    public int SourcePage { get; private set; }
    public bool RequiresReview { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<FinancialMovement> Create(
        Guid tenantId,
        Guid customerId,
        Guid documentId,
        DateOnly date,
        string? description,
        decimal amount,
        string? currency,
        string? direction,
        string? suggestedCategory,
        int sourcePage
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.TenantRequired);
        if (customerId == Guid.Empty)
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.CustomerRequired);
        if (documentId == Guid.Empty)
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.DocumentRequired);
        if (amount <= 0 || amount > MaxAmount)
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementInvalidAmount);
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementInvalidCurrency);
        if (direction is not (Credit or Debit))
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementInvalidDirection);
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementDescriptionRequired);
        if (string.IsNullOrWhiteSpace(suggestedCategory))
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementCategoryRequired);
        if (sourcePage < 1)
            return Result.Failure<FinancialMovement>(FinancialInsightsErrors.MovementInvalidSourcePage);

        var movement = new FinancialMovement
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            ImportDocumentId = documentId,
            TransactionDate = date,
            Description = Truncate(description, MaxDescriptionLength),
            Amount = amount,
            Currency = currency.ToUpperInvariant(),
            Direction = direction,
            SuggestedCategory = Truncate(suggestedCategory, MaxCategoryLength),
            SourcePage = sourcePage,
            CreatedAtUtc = DateTime.UtcNow,
        };
        movement.SetTenant(tenantId);
        return Result.Success(movement);
    }

    public new Result SetTenant(Guid tenantId)
    {
        var allowed = TenantGuard.CanAssign(TenantId, tenantId);
        if (allowed.IsSuccess)
            base.SetTenant(tenantId);
        return allowed;
    }

    void ITenantOwned.SetTenant(Guid tenantId) => SetTenant(tenantId);

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}
