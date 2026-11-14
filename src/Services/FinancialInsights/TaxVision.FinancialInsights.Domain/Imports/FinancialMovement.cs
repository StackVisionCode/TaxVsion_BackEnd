using BuildingBlocks.Domain;

namespace TaxVision.FinancialInsights.Domain.Imports;

// Suggestions are always subject to staff review before tax mapping or export.
public sealed class FinancialMovement : ITenantOwned
{
    private FinancialMovement() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
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

    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId) throw new InvalidOperationException("Tenant cannot change.");
        TenantId = tenantId;
    }

    public static FinancialMovement Create(Guid tenantId, Guid customerId, Guid documentId,
        DateOnly date, string description, decimal amount, string currency, string direction,
        string suggestedCategory, int sourcePage)
    {
        if (tenantId == Guid.Empty || customerId == Guid.Empty || documentId == Guid.Empty
            || amount <= 0 || amount > 1_000_000_000m || sourcePage < 1
            || direction is not ("credit" or "debit") || string.IsNullOrWhiteSpace(description)
            || string.IsNullOrWhiteSpace(suggestedCategory)
            || string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsLetter))
            throw new ArgumentException("Invalid financial movement.");
        return new FinancialMovement
        {
            Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, ImportDocumentId = documentId,
            TransactionDate = date, Description = description.Trim()[..Math.Min(description.Trim().Length, 500)],
            Amount = amount, Currency = currency.ToUpperInvariant(), Direction = direction,
            SuggestedCategory = suggestedCategory.Trim()[..Math.Min(suggestedCategory.Trim().Length, 120)],
            SourcePage = sourcePage, CreatedAtUtc = DateTime.UtcNow
        };
    }
}

public sealed class AiProcessingReservation : ITenantOwned
{
    private AiProcessingReservation() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ImportDocumentId { get; private set; }
    public DateTime MonthStartUtc { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public DateTime ReservedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId) throw new InvalidOperationException("Tenant cannot change.");
        TenantId = tenantId;
    }

    public static AiProcessingReservation Create(Guid tenantId, Guid documentId, string model, DateTime nowUtc) =>
        new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ImportDocumentId = documentId,
            Model = model, MonthStartUtc = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            ReservedAtUtc = nowUtc
        };

    public void RecordUsage(int inputTokens, int outputTokens)
    {
        InputTokens = Math.Max(0, inputTokens);
        OutputTokens = Math.Max(0, outputTokens);
        CompletedAtUtc = DateTime.UtcNow;
    }
}
