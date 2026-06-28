using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Customer.Domain.FiscalProfiles;

public sealed class CustomerFiscalProfile : TenantEntity
{
    private CustomerFiscalProfile() { }

    public Guid CustomerId { get; private set; }
    public FiscalSubjectKind SubjectKind { get; private set; }
    public byte[] TaxIdentifierCipher { get; private set; } = default!;
    public string TaxIdentifierBlindIndex { get; private set; } = default!;
    public string TaxIdentifierLast4 { get; private set; } = default!;
    public FilingStatus? FilingStatus { get; private set; }
    public decimal? PriorYearAgi { get; private set; }
    public bool IsReturningCustomer { get; private set; }
    public byte[]? RefundBankAccountCipher { get; private set; }
    public byte[]? RefundBankRoutingCipher { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }

    internal static Result<CustomerFiscalProfile> Create(
        Guid tenantId,
        Guid customerId,
        FiscalSubjectKind subjectKind,
        byte[] taxIdentifierCipher,
        string taxIdentifierBlindIndex,
        string taxIdentifierLast4,
        Guid updatedByUserId,
        FilingStatus? filingStatus = null,
        decimal? priorYearAgi = null,
        bool isReturningCustomer = false
    )
    {
        if (customerId == Guid.Empty)
            return Result.Failure<CustomerFiscalProfile>(
                new Error("FiscalProfile.Customer", "CustomerId is required.")
            );
        if (taxIdentifierCipher is null || taxIdentifierCipher.Length == 0)
            return Result.Failure<CustomerFiscalProfile>(
                new Error("FiscalProfile.TaxId", "Tax identifier cipher is required.")
            );
        if (string.IsNullOrWhiteSpace(taxIdentifierBlindIndex))
            return Result.Failure<CustomerFiscalProfile>(
                new Error("FiscalProfile.BlindIndex", "Tax identifier blind index is required.")
            );
        if (string.IsNullOrWhiteSpace(taxIdentifierLast4) || taxIdentifierLast4.Length != 4)
            return Result.Failure<CustomerFiscalProfile>(new Error("FiscalProfile.Last4", "Last 4 digits required."));
        if (updatedByUserId == Guid.Empty)
            return Result.Failure<CustomerFiscalProfile>(
                new Error("FiscalProfile.UpdatedBy", "UpdatedByUserId is required.")
            );

        var entity = new CustomerFiscalProfile
        {
            Id = Guid.Empty,
            CustomerId = customerId,
            SubjectKind = subjectKind,
            TaxIdentifierCipher = taxIdentifierCipher,
            TaxIdentifierBlindIndex = taxIdentifierBlindIndex,
            TaxIdentifierLast4 = taxIdentifierLast4,
            FilingStatus = filingStatus,
            PriorYearAgi = priorYearAgi,
            IsReturningCustomer = isReturningCustomer,
            UpdatedAtUtc = DateTime.UtcNow,
            UpdatedByUserId = updatedByUserId,
        };
        entity.SetTenant(tenantId);
        return Result.Success(entity);
    }

    public void Update(
        FilingStatus? filingStatus,
        decimal? priorYearAgi,
        bool isReturningCustomer,
        Guid updatedByUserId
    )
    {
        FilingStatus = filingStatus;
        PriorYearAgi = priorYearAgi;
        IsReturningCustomer = isReturningCustomer;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    public void SetRefundBank(byte[]? accountCipher, byte[]? routingCipher, Guid updatedByUserId)
    {
        RefundBankAccountCipher = accountCipher;
        RefundBankRoutingCipher = routingCipher;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    public void ReplaceTaxIdentifier(
        byte[] cipher,
        string blindIndex,
        string last4,
        FiscalSubjectKind subjectKind,
        Guid updatedByUserId
    )
    {
        TaxIdentifierCipher = cipher;
        TaxIdentifierBlindIndex = blindIndex;
        TaxIdentifierLast4 = last4;
        SubjectKind = subjectKind;
        UpdatedAtUtc = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }
}
